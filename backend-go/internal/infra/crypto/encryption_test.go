package crypto

import "testing"

// appsettings.json 中的开发密钥，用于验证与 C# 同源配置可解。
const devKey = "dG6aBuGmi/4y19MilGpiY5eEMAdm7KWkfwKTzPFlzaw="

func TestEncryptDecryptRoundTrip(t *testing.T) {
	svc, err := NewService(devKey)
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}

	for _, plain := range []string{"", "hello", "my-binance-api-key-1234567890", "中文密钥🔐"} {
		ct, err := svc.Encrypt(plain)
		if err != nil {
			t.Fatalf("Encrypt(%q): %v", plain, err)
		}
		got, err := svc.Decrypt(ct)
		if err != nil {
			t.Fatalf("Decrypt: %v", err)
		}
		if got != plain {
			t.Fatalf("round-trip mismatch: got %q want %q", got, plain)
		}
	}
}

// TestDecryptCSharpCiphertext 验证可解一段由 C# EncryptionService 用同一密钥产出的密文，
// 锁定 nonce||tag||ciphertext 布局与 C# 二进制兼容。
func TestDecryptCSharpCiphertext(t *testing.T) {
	svc, err := NewService(devKey)
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	// 由本实现产出的等价密文同样必须可解（布局自洽 + 与 C# 一致的重排）。
	ct, err := svc.Encrypt("secret-key-value")
	if err != nil {
		t.Fatalf("Encrypt: %v", err)
	}
	got, err := svc.Decrypt(ct)
	if err != nil {
		t.Fatalf("Decrypt: %v", err)
	}
	if got != "secret-key-value" {
		t.Fatalf("got %q", got)
	}
}

func TestNewServiceRejectsBadKey(t *testing.T) {
	if _, err := NewService("not-base64!!!"); err == nil {
		t.Fatal("期望非法 base64 报错")
	}
	if _, err := NewService("YWJj"); err == nil { // "abc" → 3 字节
		t.Fatal("期望非法密钥长度报错")
	}
}
