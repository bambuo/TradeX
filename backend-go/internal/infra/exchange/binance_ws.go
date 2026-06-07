package exchange

import (
	"context"
	"encoding/json"
	"strings"
	"time"

	"github.com/coder/websocket"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

const binanceWSBase = "wss://stream.binance.com:9443/ws/"

// SubscribeTrades 订阅 Binance 逐笔成交流（<symbol>@trade）。
// 每条成交回调一次 onTrade，直到 ctx 取消或连接出错返回（由调用方重连）。
func (b *BinanceAdapter) SubscribeTrades(ctx context.Context, pair string, onTrade func(Trade)) error {
	url := binanceWSBase + strings.ToLower(pair) + "@trade"
	return streamWS(ctx, url, func(data []byte) {
		var m binanceTradeMsg
		if err := json.Unmarshal(data, &m); err != nil || m.EventType != "trade" {
			return
		}
		price, _ := decimal.NewFromString(m.Price)
		qty, _ := decimal.NewFromString(m.Quantity)
		onTrade(Trade{
			Timestamp:    time.UnixMilli(m.TradeTime).UTC(),
			Price:        price,
			Quantity:     qty,
			IsBuyerMaker: m.IsBuyerMaker,
		})
	})
}

// SubscribeKlines 订阅 Binance K 线流（<symbol>@kline_<interval>）。
// 每次更新（含未闭合 kline）回调一次 onKline，闭合检测由 KlineStreamManager 负责。
func (b *BinanceAdapter) SubscribeKlines(ctx context.Context, pair, interval string, onKline func(domain.Kline)) error {
	url := binanceWSBase + strings.ToLower(pair) + "@kline_" + interval
	return streamWS(ctx, url, func(data []byte) {
		var m binanceKlineMsg
		if err := json.Unmarshal(data, &m); err != nil || m.EventType != "kline" {
			return
		}
		k := m.Kline
		onKline(domain.Kline{
			Timestamp: time.UnixMilli(k.OpenTime).UTC(),
			Open:      mustDec(k.Open),
			High:      mustDec(k.High),
			Low:       mustDec(k.Low),
			Close:     mustDec(k.Close),
			Volume:    mustDec(k.Volume),
		})
	})
}

// streamWS 建立 WS 连接并读循环，每条消息调用 handle，直到 ctx 取消或出错。
func streamWS(ctx context.Context, url string, handle func([]byte)) error {
	conn, _, err := websocket.Dial(ctx, url, nil)
	if err != nil {
		return err
	}
	defer conn.Close(websocket.StatusNormalClosure, "")
	conn.SetReadLimit(1 << 20)

	for {
		_, data, err := conn.Read(ctx)
		if err != nil {
			return err
		}
		handle(data)
	}
}

func mustDec(s string) decimal.Decimal {
	d, _ := decimal.NewFromString(s)
	return d
}

type binanceTradeMsg struct {
	EventType    string `json:"e"`
	Symbol       string `json:"s"`
	Price        string `json:"p"`
	Quantity     string `json:"q"`
	TradeTime    int64  `json:"T"`
	IsBuyerMaker bool   `json:"m"`
}

type binanceKlineMsg struct {
	EventType string `json:"e"`
	Symbol    string `json:"s"`
	Kline     struct {
		OpenTime int64  `json:"t"`
		Interval string `json:"i"`
		Open     string `json:"o"`
		Close    string `json:"c"`
		High     string `json:"h"`
		Low      string `json:"l"`
		Volume   string `json:"v"`
		IsClosed bool   `json:"x"`
	} `json:"k"`
}
