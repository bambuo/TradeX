package api

import (
	"net/http"
	"os"

	"github.com/gin-gonic/gin"
)

var apiToken string

func init() {
	apiToken = os.Getenv("TX_API_TOKEN")
}

func AuthMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		if apiToken == "" {
			c.Next()
			return
		}

		token := c.GetHeader("Authorization")
		expected := "Bearer " + apiToken
		if token != expected {
			c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "未授权访问"})
			return
		}
		c.Next()
	}
}
