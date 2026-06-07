package exchange

import (
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"time"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

// ─── 下单 ────────────────────────────────────────────────

type binancePlaceResp struct {
	OrderID          int64  `json:"orderId"`
	ExecutedQty      string `json:"executedQty"`
	CummulativeQuote string `json:"cummulativeQuoteQty"`
	Status           string `json:"status"`
	Fills            []struct {
		Price           string `json:"price"`
		Qty             string `json:"qty"`
		Commission      string `json:"commission"`
		CommissionAsset string `json:"commissionAsset"`
	} `json:"fills"`
}

// PlaceOrder 提交订单（市价/限价）。对应 C# PlaceOrderAsync。
func (b *BinanceAdapter) PlaceOrder(ctx context.Context, req OrderRequest) (OrderResult, error) {
	q := url.Values{}
	q.Set("symbol", req.Pair)
	if req.Side == domain.OrderSideBuy {
		q.Set("side", "BUY")
	} else {
		q.Set("side", "SELL")
	}
	if req.Type == domain.OrderTypeLimit {
		q.Set("type", "LIMIT")
		q.Set("timeInForce", "GTC")
		if req.Price != nil {
			q.Set("price", req.Price.String())
		}
	} else {
		q.Set("type", "MARKET")
	}
	q.Set("quantity", req.Quantity.String())
	if req.ClientOrderID != "" {
		q.Set("newClientOrderId", req.ClientOrderID)
	}

	body, status, err := b.signedPost(ctx, "/api/v3/order", q)
	if err != nil {
		return OrderResult{}, err
	}
	if status != http.StatusOK {
		return OrderResult{Success: false, Error: parseBinanceError(body)}, nil
	}

	var r binancePlaceResp
	if err := json.Unmarshal(body, &r); err != nil {
		return OrderResult{}, fmt.Errorf("解析下单响应: %w", err)
	}
	executed := safeDecimal(r.ExecutedQty, "place.filled")
	quote := safeDecimal(r.CummulativeQuote, "place.quote")
	avg := decimal.Zero
	if executed.IsPositive() {
		avg = quote.Div(executed)
	}
	fee := decimal.Zero
	feeAsset := ""
	for i, f := range r.Fills {
		c := safeDecimal(f.Commission, "fill.commission")
		fee = fee.Add(c.Abs())
		if i == 0 {
			feeAsset = f.CommissionAsset
		}
	}
	return OrderResult{
		Success:         true,
		ExchangeOrderID: strconv.FormatInt(r.OrderID, 10),
		FilledQuantity:  executed,
		AvgPrice:        avg,
		Fee:             fee,
		FeeAsset:        feeAsset,
	}, nil
}

// ─── 订单簿 ───────────────────────────────────────────────

type binanceDepth struct {
	Bids [][]string `json:"bids"`
	Asks [][]string `json:"asks"`
}

// GetOrderBook 获取订单簿快照。对应 C# GetOrderBookAsync。
func (b *BinanceAdapter) GetOrderBook(ctx context.Context, pair string, limit int) (OrderBook, error) {
	u := fmt.Sprintf("%s/api/v3/depth?symbol=%s&limit=%d", b.baseURL, pair, limit)
	body, err := b.publicGet(ctx, u)
	if err != nil {
		return OrderBook{}, err
	}
	var d binanceDepth
	if err := json.Unmarshal(body, &d); err != nil {
		return OrderBook{}, fmt.Errorf("解析订单簿: %w", err)
	}
	return OrderBook{Bids: toLevels(d.Bids), Asks: toLevels(d.Asks)}, nil
}

func toLevels(rows [][]string) []OrderBookLevel {
	out := make([]OrderBookLevel, 0, len(rows))
	for _, r := range rows {
		if len(r) < 2 {
			continue
		}
		price := safeDecimal(r[0], "depth.price")
		qty := safeDecimal(r[1], "depth.qty")
		out = append(out, OrderBookLevel{Price: price, Quantity: qty})
	}
	return out
}

// ─── 交易对规则 ───────────────────────────────────────────

type binanceExchangeInfo struct {
	Symbols []struct {
		Symbol  string `json:"symbol"`
		Filters []struct {
			FilterType  string `json:"filterType"`
			StepSize    string `json:"stepSize"`
			MinQty      string `json:"minQty"`
			MinNotional string `json:"minNotional"`
		} `json:"filters"`
	} `json:"symbols"`
}

// GetPairRules 获取全量交易对规则。对应 C# GetPairRulesAsync。
func (b *BinanceAdapter) GetPairRules(ctx context.Context) ([]PairRule, error) {
	body, err := b.publicGet(ctx, b.baseURL+"/api/v3/exchangeInfo")
	if err != nil {
		return nil, err
	}
	var info binanceExchangeInfo
	if err := json.Unmarshal(body, &info); err != nil {
		return nil, fmt.Errorf("解析 exchangeInfo: %w", err)
	}
	out := make([]PairRule, 0, len(info.Symbols))
	for _, s := range info.Symbols {
		rule := PairRule{Pair: s.Symbol}
		for _, f := range s.Filters {
			switch f.FilterType {
			case "LOT_SIZE":
				rule.StepSize = safeDecimal(f.StepSize, "rule.step")
				rule.MinQuantity = safeDecimal(f.MinQty, "rule.minqty")
			case "NOTIONAL", "MIN_NOTIONAL":
				if f.MinNotional != "" {
					rule.MinNotional = safeDecimal(f.MinNotional, "rule.minnotional")
				}
			}
		}
		out = append(out, rule)
	}
	return out, nil
}

// ─── HTTP helpers ─────────────────────────────────────────

func (b *BinanceAdapter) publicGet(ctx context.Context, fullURL string) ([]byte, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, fullURL, nil)
	if err != nil {
		return nil, err
	}
	resp, err := b.http.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("binance 返回 %d: %s", resp.StatusCode, string(body))
	}
	return body, nil
}

func (b *BinanceAdapter) signedPost(ctx context.Context, path string, params url.Values) ([]byte, int, error) {
	params.Set("timestamp", strconv.FormatInt(time.Now().UnixMilli(), 10))
	params.Set("recvWindow", "5000")
	query := params.Encode()
	mac := hmac.New(sha256.New, []byte(b.secretKey))
	mac.Write([]byte(query))
	signature := hex.EncodeToString(mac.Sum(nil))

	req, err := http.NewRequestWithContext(ctx, http.MethodPost,
		fmt.Sprintf("%s%s?%s&signature=%s", b.baseURL, path, query, signature), nil)
	if err != nil {
		return nil, 0, err
	}
	req.Header.Set("X-MBX-APIKEY", b.apiKey)

	resp, err := b.http.Do(req)
	if err != nil {
		return nil, 0, err
	}
	defer resp.Body.Close()
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, resp.StatusCode, err
	}
	return body, resp.StatusCode, nil
}
