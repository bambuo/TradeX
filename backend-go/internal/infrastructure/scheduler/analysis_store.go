package scheduler

import (
	"sync"

	"github.com/tradex/backend-go/internal/domain"
)

type TaskAnalysisStore struct {
	mu       sync.RWMutex
	store    map[string][]domain.BacktestKlineAnalysis
	channels map[string]chan domain.BacktestKlineAnalysis
}

func NewTaskAnalysisStore() *TaskAnalysisStore {
	return &TaskAnalysisStore{
		store:    make(map[string][]domain.BacktestKlineAnalysis),
		channels: make(map[string]chan domain.BacktestKlineAnalysis),
	}
}

func (s *TaskAnalysisStore) Init(taskID string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.store[taskID] = nil
	s.channels[taskID] = make(chan domain.BacktestKlineAnalysis, 1000)
}

func (s *TaskAnalysisStore) Push(taskID string, item domain.BacktestKlineAnalysis) {
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

func (s *TaskAnalysisStore) Get(taskID string) []domain.BacktestKlineAnalysis {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.store[taskID]
}

func (s *TaskAnalysisStore) Count(taskID string) int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.store[taskID])
}

func (s *TaskAnalysisStore) Remove(taskID string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	delete(s.store, taskID)
	if ch, ok := s.channels[taskID]; ok {
		close(ch)
		delete(s.channels, taskID)
	}
}

func (s *TaskAnalysisStore) Subscribe(taskID string) (<-chan domain.BacktestKlineAnalysis, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	ch, ok := s.channels[taskID]
	return ch, ok
}

func (s *TaskAnalysisStore) Exists(taskID string) bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	_, ok := s.store[taskID]
	return ok
}
