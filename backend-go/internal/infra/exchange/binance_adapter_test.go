package exchange

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/shopspring/decimal"
)

func TestMapBinanceOrder(t *testing.T) {
	o := binanceOrder{
		Symbol: "BTCUSDT", OrderID: 42, Price: "100.5", OrigQty: "2", ExecutedQty: "1.5",
		Status: "PARTIALLY_FILLED", Type: "LIMIT", Side: "BUY", Time: 1700000000000,
	}
	got := mapBinanceOrder(o)
	if got.Pair != "BTCUSDT" || got.Side != "Buy" || got.Type != "Limit" || got.Status != "PartiallyFilled" {
		t.Fatalf("映射错误: %+v", got)
	}
	if got.ExchangeOrderID != "42" {
		t.Fatalf("ExchangeOrderID = %q", got.ExchangeOrderID)
	}
	if !got.Price.Equal(decimal.RequireFromString("100.5")) || !got.FilledQuantity.Equal(decimal.RequireFromString("1.5")) {
		t.Fatalf("数量/价格映射错误: %+v", got)
	}
	if got.PlacedAt.UnixMilli() != 1700000000000 {
		t.Fatalf("PlacedAt = %v", got.PlacedAt)
	}
}

func TestMapBinanceStatusSideType(t *testing.T) {
	if mapBinanceStatus("CANCELED") != "Cancelled" || mapBinanceStatus("FILLED") != "Filled" || mapBinanceStatus("NEW") != "New" {
		t.Fatal("status 映射错误")
	}
	if mapBinanceSide("SELL") != "Sell" || mapBinanceSide("BUY") != "Buy" {
		t.Fatal("side 映射错误")
	}
	if mapBinanceType("MARKET") != "Market" || mapBinanceType("LIMIT") != "Limit" {
		t.Fatal("type 映射错误")
	}
}

func TestSignedGetAndParse(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		// 校验签名头与签名参数存在
		if r.Header.Get("X-MBX-APIKEY") != "test-key" {
			t.Errorf("缺少/错误 X-MBX-APIKEY: %q", r.Header.Get("X-MBX-APIKEY"))
		}
		q := r.URL.Query()
		if q.Get("signature") == "" || q.Get("timestamp") == "" {
			t.Errorf("缺少 signature/timestamp: %v", q)
		}
		switch r.URL.Path {
		case "/api/v3/account":
			_, _ = w.Write([]byte(`{"balances":[{"asset":"BTC","free":"1.0","locked":"0.5"},{"asset":"USDT","free":"0","locked":"0"},{"asset":"ETH","free":"0","locked":"0"}]}`))
		case "/api/v3/allOrders":
			if q.Get("symbol") != "BTCUSDT" || q.Get("limit") != "200" {
				t.Errorf("symbol/limit 参数错误: %v", q)
			}
			_, _ = w.Write([]byte(`[{"symbol":"BTCUSDT","orderId":7,"price":"50000","origQty":"0.1","executedQty":"0.1","status":"FILLED","type":"MARKET","side":"BUY","time":1700000000000}]`))
		default:
			http.NotFound(w, r)
		}
	}))
	defer srv.Close()

	ad := NewBinanceAdapter("test-key", "test-secret", false)
	ad.baseURL = srv.URL

	bals, err := ad.GetAssetBalances(context.Background())
	if err != nil {
		t.Fatalf("GetAssetBalances: %v", err)
	}
	if len(bals) != 1 || !bals["BTC"].Equal(decimal.RequireFromString("1.5")) {
		t.Fatalf("余额过滤/求和错误: %+v", bals)
	}

	orders, err := ad.GetOrderHistoryByPair(context.Background(), "BTCUSDT", 200)
	if err != nil {
		t.Fatalf("GetOrderHistoryByPair: %v", err)
	}
	if len(orders) != 1 || orders[0].Status != "Filled" || orders[0].ExchangeOrderID != "7" {
		t.Fatalf("订单解析错误: %+v", orders)
	}
}
