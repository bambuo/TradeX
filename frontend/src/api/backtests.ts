import client from './client'

export interface BacktestTask {
  id: string
  strategyId: string
  strategy_name?: string
  symbolId?: string
  timeframe?: string
  initialCapital?: number
  status: string
  phase?: string
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
  analysisCount: number
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

export interface BacktestCandleAnalysis {
  index: number
  timestamp: string
  open: number
  high: number
  low: number
  close: number
  volume: number
  indicators: Record<string, number>
  entry: boolean | null
  exit: boolean | null
  inPosition: boolean
  action: string
}

export interface AnalysisResponse {
  total: number
  page: number
  pageSize: number
  totalPages: number
  items: BacktestCandleAnalysis[]
}

export const backtestsApi = {
  start(traderId: string, strategyId: string, exchangeId: string, symbolId: string, timeframe: string, startUtc: string, endUtc: string, initialCapital?: number) {
    let url = `/traders/${traderId}/strategies/${strategyId}/backtests?exchangeId=${exchangeId}&symbolId=${encodeURIComponent(symbolId)}&timeframe=${encodeURIComponent(timeframe)}&startUtc=${encodeURIComponent(startUtc)}&endUtc=${encodeURIComponent(endUtc)}`
    if (initialCapital) url += `&initialCapital=${initialCapital}`
    return client.post<{ taskId: string; status: string; createdAt: string; strategy_name?: string; symbolId?: string; timeframe?: string }>(url)
  },
  getTasks(traderId: string, strategyId: string) {
    return client.get<BacktestTask[]>(`/traders/${traderId}/strategies/${strategyId}/backtests/tasks`)
  },
  getTask(traderId: string, strategyId: string, taskId: string) {
    return client.get<BacktestTask>(`/traders/${traderId}/strategies/${strategyId}/backtests/tasks/${taskId}`)
  },
  getResult(traderId: string, strategyId: string, taskId: string) {
    return client.get<BacktestResult>(`/traders/${traderId}/strategies/${strategyId}/backtests/tasks/${taskId}/result`)
  },
  getAnalysis(traderId: string, strategyId: string, taskId: string, page = 1, pageSize = 100) {
    return client.get<AnalysisResponse>(`/traders/${traderId}/strategies/${strategyId}/backtests/tasks/${taskId}/analysis?page=${page}&pageSize=${pageSize}`)
  }
}
