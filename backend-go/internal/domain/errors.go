package domain

import "errors"

var (
	ErrNotFound         = errors.New("resource not found")
	ErrInvalidInput     = errors.New("invalid input")
	ErrInvalidOperation = errors.New("invalid operation")
	ErrInsufficientData = errors.New("insufficient data")
	ErrConflict         = errors.New("resource conflict")
)
