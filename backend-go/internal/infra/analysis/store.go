package analysis

import (
	"sync"

	bt "tradex/internal/domain/backtest"
)

type Store struct {
	mu       sync.RWMutex
	store    map[string][]bt.BacktestKlineAnalysis
	channels map[string]chan bt.BacktestKlineAnalysis
}

func NewStore() *Store {
	return &Store{
		store:    make(map[string][]bt.BacktestKlineAnalysis),
		channels: make(map[string]chan bt.BacktestKlineAnalysis),
	}
}

func (s *Store) Init(taskID string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.store[taskID] = nil
	s.channels[taskID] = make(chan bt.BacktestKlineAnalysis, 1000)
}

func (s *Store) Push(taskID string, item bt.BacktestKlineAnalysis) {
	s.mu.Lock()
	s.store[taskID] = append(s.store[taskID], item)
	ch := s.channels[taskID]
	s.mu.Unlock()

	if ch != nil {
		select {
		case ch <- item:
		default:
		}
	}
}

func (s *Store) Get(taskID string) []bt.BacktestKlineAnalysis {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.store[taskID]
}

func (s *Store) ConsumeFrom(taskID string, fromIndex int) (batch []bt.BacktestKlineAnalysis, total int) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	items := s.store[taskID]
	total = len(items)
	if total > fromIndex {
		batch = make([]bt.BacktestKlineAnalysis, total-fromIndex)
		copy(batch, items[fromIndex:])
	}
	return
}

func (s *Store) Count(taskID string) int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.store[taskID])
}

func (s *Store) Remove(taskID string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	delete(s.store, taskID)
	if ch, ok := s.channels[taskID]; ok {
		close(ch)
		delete(s.channels, taskID)
	}
}

func (s *Store) Subscribe(taskID string) (<-chan bt.BacktestKlineAnalysis, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	ch, ok := s.channels[taskID]
	return ch, ok
}

func (s *Store) Exists(taskID string) bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	_, ok := s.store[taskID]
	return ok
}
