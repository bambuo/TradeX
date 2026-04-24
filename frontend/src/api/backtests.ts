import client from './client'

export interface BacktestTask {
  id: string
  strategyId: string
  status: string
  startAtUtc: string
  endAtUtc: string
  createdAtUtc: string
  completedAtUtc: string | null
}

export interface BacktestResult {
  totalReturnPercent: number
  annualizedReturnPercent: number
  maxDrawdownPercent: number
  winRate: number
  totalTrades: number
  sharpeRatio: number
  profitLossRatio: number
  trades: BacktestTrade[]
}

export interface BacktestTrade {
  entryTime: string
  exitTime: string
  entryPrice: number
  exitPrice: number
  quantity: number
  pnl: number
  pnlPercent: number
}

export const backtestsApi = {
  start(traderId: string, strategyId: string, startUtc: string, endUtc: string) {
    return client.post<{ taskId: string; status: string; createdAt: string }>(
      `/traders/${traderId}/strategies/${strategyId}/backtests?startUtc=${encodeURIComponent(startUtc)}&endUtc=${encodeURIComponent(endUtc)}`
    )
  },
  getTasks(traderId: string, strategyId: string) {
    return client.get<BacktestTask[]>(`/traders/${traderId}/strategies/${strategyId}/backtests/tasks`)
  },
  getTask(traderId: string, strategyId: string, taskId: string) {
    return client.get<BacktestTask>(`/traders/${traderId}/strategies/${strategyId}/backtests/tasks/${taskId}`)
  },
  getResult(traderId: string, strategyId: string, taskId: string) {
    return client.get<BacktestResult>(`/traders/${traderId}/strategies/${strategyId}/backtests/tasks/${taskId}/result`)
  }
}
