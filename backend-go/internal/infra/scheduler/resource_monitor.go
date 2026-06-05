package scheduler

import (
	"context"
	"sync/atomic"
	"time"

	"github.com/shirou/gopsutil/v4/cpu"
	"github.com/shirou/gopsutil/v4/mem"
)

type ResourceMonitor struct {
	allowedConcurrency atomic.Int32
	maxConcurrency     int
}

func NewResourceMonitor(ctx context.Context, maxConcurrency int) *ResourceMonitor {
	rm := &ResourceMonitor{
		maxConcurrency:     maxConcurrency,
	}
	rm.allowedConcurrency.Store(int32(maxConcurrency))
	go rm.monitorLoop(ctx)
	return rm
}

func (rm *ResourceMonitor) AllowedConcurrency() int {
	return int(rm.allowedConcurrency.Load())
}

func (rm *ResourceMonitor) monitorLoop(ctx context.Context) {
	ticker := time.NewTicker(5 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			memPercent := rm.getMemPercent()
			cpuPercent := rm.getCPUPercent()

			allowed := rm.maxConcurrency

			switch {
			case memPercent > 90 || cpuPercent > 90:
				allowed = 1
			case memPercent > 75 || cpuPercent > 75:
				allowed = max(1, rm.maxConcurrency-2)
			case memPercent > 50 || cpuPercent > 50:
				allowed = max(1, rm.maxConcurrency-1)
			}

			rm.allowedConcurrency.Store(int32(allowed))
		}
	}
}

func (rm *ResourceMonitor) getMemPercent() float64 {
	v, err := mem.VirtualMemory()
	if err != nil {
		return 0
	}
	return v.UsedPercent
}

func (rm *ResourceMonitor) getCPUPercent() float64 {
	percent, err := cpu.Percent(0, false)
	if err != nil || len(percent) == 0 {
		return 0
	}
	return percent[0]
}
