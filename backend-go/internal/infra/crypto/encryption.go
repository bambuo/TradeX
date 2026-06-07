// Package crypto 提供与 C# TradeX.Infrastructure.Services.EncryptionService 二进制兼容的
// AES-GCM 加解密。密文布局为 base64( nonce[12] || tag[16] || ciphertext )，密钥为 base64 编码。
//
// 保持与 C# 完全一致是硬性要求：Worker 进程需要解密由 API 进程写入数据库的交易所密钥。
package crypto

import (
	"crypto/aes"
	"crypto/cipher"
	"crypto/rand"
	"encoding/base64"
	"errors"
	"fmt"
	"io"
)

const (
	nonceSize = 12
	tagSize   = 16
)

// Service 是 AES-GCM 加解密服务。
type Service struct {
	key []byte
}

// NewService 用 base64 编码的密钥构造服务。密钥解码后长度须为 16/24/32 字节（AES-128/192/256）。
func NewService(base64Key string) (*Service, error) {
	key, err := base64.StdEncoding.DecodeString(base64Key)
	if err != nil {
		return nil, fmt.Errorf("解码加密密钥: %w", err)
	}
	switch len(key) {
	case 16, 24, 32:
	default:
		return nil, fmt.Errorf("加密密钥长度非法: %d 字节（须为 16/24/32）", len(key))
	}
	return &Service{key: key}, nil
}

// Encrypt 返回 base64( nonce || tag || ciphertext )。
func (s *Service) Encrypt(plaintext string) (string, error) {
	gcm, err := s.newGCM()
	if err != nil {
		return "", err
	}

	nonce := make([]byte, nonceSize)
	if _, err := io.ReadFull(rand.Reader, nonce); err != nil {
		return "", fmt.Errorf("生成 nonce: %w", err)
	}

	// Go 的 Seal 产出 ciphertext||tag，需重排为 C# 的 nonce||tag||ciphertext。
	sealed := gcm.Seal(nil, nonce, []byte(plaintext), nil)
	ct, tag := sealed[:len(sealed)-tagSize], sealed[len(sealed)-tagSize:]

	combined := make([]byte, 0, nonceSize+tagSize+len(ct))
	combined = append(combined, nonce...)
	combined = append(combined, tag...)
	combined = append(combined, ct...)
	return base64.StdEncoding.EncodeToString(combined), nil
}

// Decrypt 解析 base64( nonce || tag || ciphertext ) 并返回明文。
func (s *Service) Decrypt(ciphertext string) (string, error) {
	combined, err := base64.StdEncoding.DecodeString(ciphertext)
	if err != nil {
		return "", fmt.Errorf("解码密文: %w", err)
	}
	if len(combined) < nonceSize+tagSize {
		return "", errors.New("密文长度不足")
	}

	nonce := combined[:nonceSize]
	tag := combined[nonceSize : nonceSize+tagSize]
	ct := combined[nonceSize+tagSize:]

	gcm, err := s.newGCM()
	if err != nil {
		return "", err
	}

	// Go 的 Open 期望 ciphertext||tag。
	sealed := make([]byte, 0, len(ct)+tagSize)
	sealed = append(sealed, ct...)
	sealed = append(sealed, tag...)

	plain, err := gcm.Open(nil, nonce, sealed, nil)
	if err != nil {
		return "", fmt.Errorf("AES-GCM 解密失败: %w", err)
	}
	return string(plain), nil
}

func (s *Service) newGCM() (cipher.AEAD, error) {
	block, err := aes.NewCipher(s.key)
	if err != nil {
		return nil, fmt.Errorf("初始化 AES: %w", err)
	}
	gcm, err := cipher.NewGCM(block) // 默认 nonce=12、tag=16，与 AesGcm(_key, 16) 一致
	if err != nil {
		return nil, fmt.Errorf("初始化 GCM: %w", err)
	}
	return gcm, nil
}
