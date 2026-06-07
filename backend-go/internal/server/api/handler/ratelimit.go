package handler

import (
	"net/http"
	"os"
	"strconv"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
)

var (
	rateLimitOnce sync.Once
	rateLimitRPS  int
)

func init() {
	rateLimitOnce.Do(func() {
		raw := os.Getenv("TX_RATE_LIMIT")
		if raw == "" {
			rateLimitRPS = 100
			return
		}
		if v, err := strconv.Atoi(raw); err == nil && v > 0 {
			rateLimitRPS = v
		} else {
			rateLimitRPS = 100
		}
	})
}

type visitor struct {
	mu     sync.Mutex
	window []time.Time
}

var visitors sync.Map

func RateLimitMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		key := c.ClientIP()
		now := time.Now()

		v, _ := visitors.LoadOrStore(key, &visitor{})
		vis := v.(*visitor)

		vis.mu.Lock()
		cutoff := now.Add(-time.Second)
		j := 0
		for j < len(vis.window) && vis.window[j].Before(cutoff) {
			j++
		}
		vis.window = vis.window[j:]

		if len(vis.window) >= rateLimitRPS {
			vis.mu.Unlock()
			c.AbortWithStatusJSON(http.StatusTooManyRequests, gin.H{"error": "请求过于频繁，请稍后重试"})
			return
		}

		vis.window = append(vis.window, now)
		vis.mu.Unlock()
		c.Next()
	}
}
