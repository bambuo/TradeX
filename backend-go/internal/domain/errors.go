package domain

import "errors"

var (
	ErrNotFound         = errors.New("资源不存在")
	ErrInvalidInput     = errors.New("非法输入")
	ErrInvalidOperation = errors.New("非法操作")
	ErrInsufficientData = errors.New("数据不足")
	ErrConflict         = errors.New("资源冲突")
)
