package domain

import (
	"time"

	"github.com/google/uuid"
)

// Exchange 交易所配置（含加密后的密钥）。对应 C# TradeX.Core.Models.Exchange。
//
// Worker 进程仅读取（GetAllEnabled）并解密密钥用于建 client，不修改其状态；
// 因此这里只保留字段与必要的领域判断，状态变更/领域事件由 API 进程负责。
type Exchange struct {
	ID                  uuid.UUID      `json:"id"`
	Name                string         `json:"name"`
	Type                ExchangeType   `json:"type"`
	APIKeyEncrypted     string         `json:"apiKeyEncrypted"`
	SecretKeyEncrypted  string         `json:"secretKeyEncrypted"`
	PassphraseEncrypted *string        `json:"passphraseEncrypted,omitempty"`
	Status              ExchangeStatus `json:"status"`
	LastTestedAt        *time.Time     `json:"lastTestedAt,omitempty"`
	TestResult          *string        `json:"testResult,omitempty"`
	CreatedBy           uuid.UUID      `json:"createdBy"`
	CreatedAt           time.Time      `json:"createdAt"`
	UpdatedAt           time.Time      `json:"updatedAt"`
}

// IsEnabled 报告交易所是否处于启用态。
func (e *Exchange) IsEnabled() bool { return e.Status == ExchangeStatusEnabled }
