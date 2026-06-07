package router

import (
	"github.com/gin-gonic/gin"

	"tradex/internal/server/api/handler"
)

func RegisterRoutes(r *gin.Engine, h *handler.BacktestHandler, middlewares ...gin.HandlerFunc) {
	for _, m := range middlewares {
		r.Use(m)
	}
	h.RegisterRoutes(r)
}
