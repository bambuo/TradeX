package trading

import (
	"github.com/rs/zerolog"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

// ClientFactory 按交易所类型构造客户端（*exchange.Factory 即满足）。
type ClientFactory interface {
	CreateClient(t domain.ExchangeType, apiKey, secretKey string, passphrase *string) (exchange.Client, error)
}

// Decryptor 解密交易所密钥（*crypto.Service 即满足）。
type Decryptor interface {
	Decrypt(ciphertext string) (string, error)
}

// ExchangeClientProvider 封装"解密密钥 + 构造客户端"，对应 C# 各处的 TryCreateClient。
type ExchangeClientProvider struct {
	factory ClientFactory
	enc     Decryptor
	log     zerolog.Logger
}

// NewExchangeClientProvider 构造客户端提供器。
func NewExchangeClientProvider(factory ClientFactory, enc Decryptor, log zerolog.Logger) *ExchangeClientProvider {
	return &ExchangeClientProvider{factory: factory, enc: enc, log: log}
}

// TryCreate 解密密钥并构造客户端；任一步失败返回 nil（并记录警告），对应 C# 返回 null 的语义。
func (p *ExchangeClientProvider) TryCreate(ex *domain.Exchange) exchange.Client {
	apiKey, err := p.enc.Decrypt(ex.APIKeyEncrypted)
	if err != nil {
		p.log.Warn().Err(err).Str("exchange_id", ex.ID.String()).Msg("创建交易所客户端失败：解密 apiKey")
		return nil
	}
	secretKey, err := p.enc.Decrypt(ex.SecretKeyEncrypted)
	if err != nil {
		p.log.Warn().Err(err).Str("exchange_id", ex.ID.String()).Msg("创建交易所客户端失败：解密 secretKey")
		return nil
	}
	var passphrase *string
	if ex.PassphraseEncrypted != nil {
		pp, err := p.enc.Decrypt(*ex.PassphraseEncrypted)
		if err != nil {
			p.log.Warn().Err(err).Str("exchange_id", ex.ID.String()).Msg("创建交易所客户端失败：解密 passphrase")
			return nil
		}
		passphrase = &pp
	}
	client, err := p.factory.CreateClient(ex.Type, apiKey, secretKey, passphrase)
	if err != nil {
		p.log.Warn().Err(err).Str("exchange_id", ex.ID.String()).Msg("创建交易所客户端失败")
		return nil
	}
	return client
}
