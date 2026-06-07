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

// BinanceAdapter 是 Binance 现货的带认证 REST 客户端，对应 C# BinanceClientAdapter。
// 直接实现签名请求（HMAC-SHA256），不依赖第三方 SDK。
type BinanceAdapter struct {
	apiKey    string
	secretKey string
	baseURL   string
	http      *http.Client
}

// NewBinanceAdapter 用凭证构造适配器。isTestnet 时指向测试网。
func NewBinanceAdapter(apiKey, secretKey string, isTestnet bool) *BinanceAdapter {
	base := "https://api.binance.com"
	if isTestnet {
		base = "https://testnet.binance.vision"
	}
	return &BinanceAdapter{
		apiKey:    apiKey,
		secretKey: secretKey,
		baseURL:   base,
		http:      &http.Client{Timeout: 30 * time.Second},
	}
}

func (b *BinanceAdapter) Type() domain.ExchangeType { return domain.ExchangeTypeBinance }

// Ping 检测交易所连通性（公开端点 /api/v3/ping）。对应 C# TestConnection 的连通性核心。
func (b *BinanceAdapter) Ping(ctx context.Context) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, b.baseURL+"/api/v3/ping", nil)
	if err != nil {
		return err
	}
	resp, err := b.http.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("binance ping 返回 %d", resp.StatusCode)
	}
	return nil
}

// ─── 账户 ────────────────────────────────────────────────

type binanceBalance struct {
	Asset  string `json:"asset"`
	Free   string `json:"free"`
	Locked string `json:"locked"`
}

type binanceAccount struct {
	Balances []binanceBalance `json:"balances"`
}

// GetAssetBalances 返回总额（free+locked）>0 的资产。对应 C# GetAssetBalancesAsync。
func (b *BinanceAdapter) GetAssetBalances(ctx context.Context) (map[string]decimal.Decimal, error) {
	body, err := b.signedGet(ctx, "/api/v3/account", url.Values{})
	if err != nil {
		return nil, err
	}
	var acct binanceAccount
	if err := json.Unmarshal(body, &acct); err != nil {
		return nil, fmt.Errorf("解析账户余额: %w", err)
	}

	out := make(map[string]decimal.Decimal)
	for _, bal := range acct.Balances {
		free := safeDecimal(bal.Free, "balance.free")
		locked := safeDecimal(bal.Locked, "balance.locked")
		total := free.Add(locked)
		if total.IsPositive() {
			out[bal.Asset] = total
		}
	}
	return out, nil
}

// ─── 历史订单 ─────────────────────────────────────────────

type binanceOrder struct {
	Symbol      string `json:"symbol"`
	OrderID     int64  `json:"orderId"`
	Price       string `json:"price"`
	OrigQty     string `json:"origQty"`
	ExecutedQty string `json:"executedQty"`
	Status      string `json:"status"`
	Type        string `json:"type"`
	Side        string `json:"side"`
	Time        int64  `json:"time"`
}

// GetOrderHistoryByPair 返回指定交易对最多 limit 条历史订单。对应 C# GetOrderHistoryByPairAsync。
func (b *BinanceAdapter) GetOrderHistoryByPair(ctx context.Context, pair string, limit int) ([]ExchangeOrderDTO, error) {
	q := url.Values{}
	q.Set("symbol", pair)
	q.Set("limit", strconv.Itoa(limit))
	body, err := b.signedGet(ctx, "/api/v3/allOrders", q)
	if err != nil {
		return nil, err
	}
	var orders []binanceOrder
	if err := json.Unmarshal(body, &orders); err != nil {
		return nil, fmt.Errorf("解析历史订单: %w", err)
	}

	out := make([]ExchangeOrderDTO, 0, len(orders))
	for _, o := range orders {
		out = append(out, mapBinanceOrder(o))
	}
	return out, nil
}

func mapBinanceOrder(o binanceOrder) ExchangeOrderDTO {
	price := safeDecimal(o.Price, "order.price")
	qty := safeDecimal(o.OrigQty, "order.qty")
	filled := safeDecimal(o.ExecutedQty, "order.filled")
	return ExchangeOrderDTO{
		Pair:            o.Symbol,
		Side:            mapBinanceSide(o.Side),
		Type:            mapBinanceType(o.Type),
		Status:          mapBinanceStatus(o.Status),
		Price:           price,
		Quantity:        qty,
		FilledQuantity:  filled,
		ExchangeOrderID: strconv.FormatInt(o.OrderID, 10),
		PlacedAt:        time.UnixMilli(o.Time).UTC(),
	}
}

func mapBinanceSide(side string) string {
	if side == "BUY" {
		return "Buy"
	}
	return "Sell"
}

func mapBinanceType(t string) string {
	if t == "LIMIT" {
		return "Limit"
	}
	return "Market"
}

func mapBinanceStatus(s string) string {
	switch s {
	case "NEW":
		return "New"
	case "PARTIALLY_FILLED":
		return "PartiallyFilled"
	case "FILLED":
		return "Filled"
	case "CANCELED":
		return "Cancelled"
	case "EXPIRED":
		return "Expired"
	default:
		return s
	}
}

// ─── 订单查询（对账用）──────────────────────────────────────

// GetOpenOrders 返回账户所有未结订单。对应 C# GetOpenOrdersAsync。
func (b *BinanceAdapter) GetOpenOrders(ctx context.Context) ([]ExchangeOrderDTO, error) {
	body, err := b.signedGet(ctx, "/api/v3/openOrders", url.Values{})
	if err != nil {
		return nil, err
	}
	var orders []binanceOrder
	if err := json.Unmarshal(body, &orders); err != nil {
		return nil, fmt.Errorf("解析未结订单: %w", err)
	}
	out := make([]ExchangeOrderDTO, 0, len(orders))
	for _, o := range orders {
		out = append(out, mapBinanceOrder(o))
	}
	return out, nil
}

// binanceOrderDetail 是 /api/v3/order 的返回，含累计成交额用于推导均价。
type binanceOrderDetail struct {
	OrderID          int64  `json:"orderId"`
	ExecutedQty      string `json:"executedQty"`
	CummulativeQuote string `json:"cummulativeQuoteQty"`
	Status           string `json:"status"`
}

// GetOrder 按交易所订单号查询订单状态。对应 C# GetOrderAsync。
func (b *BinanceAdapter) GetOrder(ctx context.Context, pair, exchangeOrderID string) (OrderResult, error) {
	q := url.Values{}
	q.Set("symbol", pair)
	q.Set("orderId", exchangeOrderID)
	return b.queryOrder(ctx, q, exchangeOrderID)
}

// GetOrderByClientOrderID 凭 ClientOrderId 反查（Binance: origClientOrderId）。对应 C# GetOrderByClientOrderIdAsync。
func (b *BinanceAdapter) GetOrderByClientOrderID(ctx context.Context, pair, clientOrderID string) (OrderResult, error) {
	q := url.Values{}
	q.Set("symbol", pair)
	q.Set("origClientOrderId", clientOrderID)
	return b.queryOrder(ctx, q, "")
}

// queryOrder 执行签名查询；交易所明确失败 → Success=false（非 error），传输异常 → error。
func (b *BinanceAdapter) queryOrder(ctx context.Context, q url.Values, fallbackID string) (OrderResult, error) {
	body, status, err := b.signedGetRaw(ctx, "/api/v3/order", q)
	if err != nil {
		return OrderResult{}, err
	}
	if status != http.StatusOK {
		return OrderResult{Success: false, Error: parseBinanceError(body)}, nil
	}
	var d binanceOrderDetail
	if err := json.Unmarshal(body, &d); err != nil {
		return OrderResult{}, fmt.Errorf("解析订单详情: %w", err)
	}
	filled := safeDecimal(d.ExecutedQty, "order.detail.filled")
	quote := safeDecimal(d.CummulativeQuote, "order.detail.quote")
	avg := decimal.Zero
	if filled.IsPositive() {
		avg = quote.Div(filled)
	}
	exchangeOrderID := fallbackID
	if d.OrderID != 0 {
		exchangeOrderID = strconv.FormatInt(d.OrderID, 10)
	}
	return OrderResult{
		Success:         true,
		ExchangeOrderID: exchangeOrderID,
		FilledQuantity:  filled,
		AvgPrice:        avg,
	}, nil
}

type binanceError struct {
	Code int    `json:"code"`
	Msg  string `json:"msg"`
}

func parseBinanceError(body []byte) string {
	var e binanceError
	if json.Unmarshal(body, &e) == nil && e.Msg != "" {
		return e.Msg
	}
	return string(body)
}

// ─── 签名请求 ─────────────────────────────────────────────

// signedGet 发起带签名的 GET，非 200 视为错误（适用于余额/历史等"失败即异常"的场景）。
func (b *BinanceAdapter) signedGet(ctx context.Context, path string, params url.Values) ([]byte, error) {
	body, status, err := b.signedGetRaw(ctx, path, params)
	if err != nil {
		return nil, err
	}
	if status != http.StatusOK {
		return nil, fmt.Errorf("binance %s 返回 %d: %s", path, status, string(body))
	}
	return body, nil
}

// signedGetRaw 发起带 HMAC-SHA256 签名的 GET，返回响应体与状态码（不因非 200 报错）。
func (b *BinanceAdapter) signedGetRaw(ctx context.Context, path string, params url.Values) ([]byte, int, error) {
	params.Set("timestamp", strconv.FormatInt(time.Now().UnixMilli(), 10))
	params.Set("recvWindow", "5000")

	query := params.Encode()
	mac := hmac.New(sha256.New, []byte(b.secretKey))
	mac.Write([]byte(query))
	signature := hex.EncodeToString(mac.Sum(nil))
	fullURL := fmt.Sprintf("%s%s?%s&signature=%s", b.baseURL, path, query, signature)

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, fullURL, nil)
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
