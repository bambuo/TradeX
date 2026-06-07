package handler

import (
	"net/http"
	"os"
	"sync"

	"github.com/gin-gonic/gin"
)

var (
	apiTokenOnce sync.Once
	apiToken     string
)

func getAPIToken() string {
	apiTokenOnce.Do(func() {
		apiToken = os.Getenv("TX_API_TOKEN")
	})
	return apiToken
}

func AuthMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		token := getAPIToken()
		if token == "" {
			c.Next()
			return
		}
		expected := "Bearer " + token
		if c.GetHeader("Authorization") != expected {
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "未授权访问"})
			return
		}
		c.Next()
	}
}
