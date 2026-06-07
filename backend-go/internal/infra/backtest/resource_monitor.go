package backtest

import (
	"context"
	"sync/atomic"
	"time"

	"github.com/shirou/gopsutil/v4/cpu"
	"github.com/shirou/gopsutil/v4/mem"
)

type ResourceMonitorConfig struct {
	MonitorIntervalSec int
	CpuWarningPercent  int
	CpuCriticalPercent int
	CpuAbsolutePercent int
	MemWarningPercent  int
	MemCriticalPercent int
	MemAbsolutePercent int
}

func DefaultResourceMonitorConfig() ResourceMonitorConfig {
	return ResourceMonitorConfig{
		MonitorIntervalSec: 5,
		CpuWarningPercent:  50,
		CpuCriticalPercent: 75,
		CpuAbsolutePercent: 90,
		MemWarningPercent:  50,
		MemCriticalPercent: 75,
		MemAbsolutePercent: 90,
	}
}

type ResourceMonitor struct {
	allowedConcurrency atomic.Int32
	maxConcurrency     int
	cfg                ResourceMonitorConfig
}

func NewResourceMonitor(ctx context.Context, maxConcurrency int, cfg ...ResourceMonitorConfig) *ResourceMonitor {
	rm := &ResourceMonitor{
		maxConcurrency: maxConcurrency,
		cfg:            DefaultResourceMonitorConfig(),
	}
	if len(cfg) > 0 {
		rm.cfg = cfg[0]
	}
	rm.allowedConcurrency.Store(int32(maxConcurrency))
	go rm.monitorLoop(ctx)
	return rm
}

func (rm *ResourceMonitor) AllowedConcurrency() int {
	return int(rm.allowedConcurrency.Load())
}

func (rm *ResourceMonitor) monitorLoop(ctx context.Context) {
	ticker := time.NewTicker(time.Duration(rm.cfg.MonitorIntervalSec) * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			memPercent := rm.getMemPercent()
			cpuPercent := rm.getCPUPercent()
			abs := rm.cfg.MemAbsolutePercent
			cpuAbs := rm.cfg.CpuAbsolutePercent

			allowed := rm.maxConcurrency

			switch {
			case memPercent > float64(abs) || cpuPercent > float64(cpuAbs):
				allowed = 1
			case memPercent > float64(rm.cfg.MemCriticalPercent) || cpuPercent > float64(rm.cfg.CpuCriticalPercent):
				allowed = max(1, rm.maxConcurrency-2)
			case memPercent > float64(rm.cfg.MemWarningPercent) || cpuPercent > float64(rm.cfg.CpuWarningPercent):
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
