package domain

import "errors"

var (
	ErrNotFound     = errors.New("资源不存在")
	ErrInvalidInput = errors.New("非法输入")
	ErrConflict     = errors.New("资源冲突")
)
