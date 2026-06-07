package router

import (
	"tradex/internal/handler"

	"github.com/gin-gonic/gin"
)

func RegisterRoutes(r *gin.Engine, h *handler.BacktestHandler, middlewares ...gin.HandlerFunc) {
	for _, m := range middlewares {
		r.Use(m)
	}
	h.RegisterRoutes(r)
}
