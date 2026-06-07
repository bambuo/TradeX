package trading

import (
	"context"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

// ─── fake repos ───

type fakeOrderRepo struct {
	byID          map[uuid.UUID]*domain.Order
	byExchOrderID map[string]*domain.Order
	pending       map[uuid.UUID][]*domain.Order
	updated       []*domain.Order
	added         []*domain.Order
}

func newFakeOrderRepo() *fakeOrderRepo {
	return &fakeOrderRepo{byID: map[uuid.UUID]*domain.Order{}, byExchOrderID: map[string]*domain.Order{}, pending: map[uuid.UUID][]*domain.Order{}}
}

func (r *fakeOrderRepo) GetByID(_ context.Context, id uuid.UUID) (*domain.Order, error) {
	return r.byID[id], nil
}
func (r *fakeOrderRepo) GetByExchangeOrderID(_ context.Context, eid string) (*domain.Order, error) {
	return r.byExchOrderID[eid], nil
}
func (r *fakeOrderRepo) GetPendingByExchange(_ context.Context, exID uuid.UUID) ([]*domain.Order, error) {
	return r.pending[exID], nil
}
func (r *fakeOrderRepo) HasActiveBuy(context.Context, uuid.UUID, string) (bool, error) {
	return false, nil
}
func (r *fakeOrderRepo) Add(_ context.Context, o *domain.Order) error {
	r.added = append(r.added, o)
	return nil
}
func (r *fakeOrderRepo) Update(_ context.Context, o *domain.Order) error {
	r.updated = append(r.updated, o)
	return nil
}

type fakePositionRepo struct {
	byOpeningOrder  map[uuid.UUID]*domain.Position
	byID            map[uuid.UUID]*domain.Position
	openByStratPair map[string][]*domain.Position
	allOpen         []*domain.Position
	added           []*domain.Position
	updated         []*domain.Position
	closedSince     []*domain.Position
	openByTrader    map[uuid.UUID][]*domain.Position
}

func newFakePositionRepo() *fakePositionRepo {
	return &fakePositionRepo{
		byOpeningOrder:  map[uuid.UUID]*domain.Position{},
		byID:            map[uuid.UUID]*domain.Position{},
		openByStratPair: map[string][]*domain.Position{},
		openByTrader:    map[uuid.UUID][]*domain.Position{},
	}
}

func (r *fakePositionRepo) GetClosedByTraderIDSince(context.Context, uuid.UUID, time.Time) ([]*domain.Position, error) {
	return r.closedSince, nil
}

func (r *fakePositionRepo) GetOpenByTraderID(_ context.Context, tid uuid.UUID) ([]*domain.Position, error) {
	return r.openByTrader[tid], nil
}

func (r *fakePositionRepo) GetByID(_ context.Context, id uuid.UUID) (*domain.Position, error) {
	return r.byID[id], nil
}
func (r *fakePositionRepo) GetAllOpen(context.Context) ([]*domain.Position, error) {
	return r.allOpen, nil
}
func (r *fakePositionRepo) GetByStrategyID(context.Context, uuid.UUID) ([]*domain.Position, error) {
	return nil, nil
}
func (r *fakePositionRepo) GetByOpeningOrderID(_ context.Context, id uuid.UUID) (*domain.Position, error) {
	return r.byOpeningOrder[id], nil
}
func (r *fakePositionRepo) GetOpenByStrategyAndPair(_ context.Context, sid uuid.UUID, pair string) ([]*domain.Position, error) {
	return r.openByStratPair[sid.String()+"|"+pair], nil
}
func (r *fakePositionRepo) Add(_ context.Context, p *domain.Position) error {
	r.added = append(r.added, p)
	r.byID[p.ID] = p
	if p.OpeningOrderID != nil {
		r.byOpeningOrder[*p.OpeningOrderID] = p
	}
	return nil
}
func (r *fakePositionRepo) Update(_ context.Context, p *domain.Position) error {
	r.updated = append(r.updated, p)
	return nil
}

type fakeExchangeRepo struct {
	list map[uuid.UUID]*domain.Exchange
}

func (r *fakeExchangeRepo) GetByID(_ context.Context, id uuid.UUID) (*domain.Exchange, error) {
	return r.list[id], nil
}
func (r *fakeExchangeRepo) GetAllEnabled(context.Context) ([]*domain.Exchange, error) {
	out := make([]*domain.Exchange, 0, len(r.list))
	for _, e := range r.list {
		out = append(out, e)
	}
	return out, nil
}

// ─── fake event bus ───

type recordingBus struct{ events []domain.DomainEvent }

func (b *recordingBus) Publish(_ context.Context, e domain.DomainEvent) error {
	b.events = append(b.events, e)
	return nil
}

// ─── fake exchange client + factory + decryptor ───

type fakeExClient struct {
	balances   map[string]decimal.Decimal
	openOrders []exchange.ExchangeOrderDTO
	getOrder   func(pair, eid string) (exchange.OrderResult, error)
}

func (c *fakeExClient) Type() domain.ExchangeType { return domain.ExchangeTypeBinance }
func (c *fakeExClient) GetAssetBalances(context.Context) (map[string]decimal.Decimal, error) {
	return c.balances, nil
}
func (c *fakeExClient) GetOrderHistoryByPair(context.Context, string, int) ([]exchange.ExchangeOrderDTO, error) {
	return nil, nil
}
func (c *fakeExClient) GetOpenOrders(context.Context) ([]exchange.ExchangeOrderDTO, error) {
	return c.openOrders, nil
}
func (c *fakeExClient) GetOrder(_ context.Context, pair, eid string) (exchange.OrderResult, error) {
	if c.getOrder != nil {
		return c.getOrder(pair, eid)
	}
	return exchange.OrderResult{}, nil
}
func (c *fakeExClient) GetOrderByClientOrderID(context.Context, string, string) (exchange.OrderResult, error) {
	return exchange.OrderResult{Success: false, Error: "not_supported"}, nil
}
func (c *fakeExClient) PlaceOrder(context.Context, exchange.OrderRequest) (exchange.OrderResult, error) {
	return exchange.OrderResult{}, nil
}
func (c *fakeExClient) GetOrderBook(context.Context, string, int) (exchange.OrderBook, error) {
	return exchange.OrderBook{}, nil
}
func (c *fakeExClient) GetPairRules(context.Context) ([]exchange.PairRule, error) {
	return nil, nil
}

type fakeFactory struct{ client exchange.Client }

func (f *fakeFactory) CreateClient(domain.ExchangeType, string, string, *string) (exchange.Client, error) {
	return f.client, nil
}

type fakeDecryptor struct{}

func (fakeDecryptor) Decrypt(s string) (string, error) { return s, nil }

func dn(s string) decimal.Decimal { return decimal.RequireFromString(s) }

func balanceMap(asset, qty string) map[string]decimal.Decimal {
	return map[string]decimal.Decimal{asset: decimal.RequireFromString(qty)}
}
