import client from './client'

export interface BacktestTask {
  id: string
  strategyId: string
  exchangeId: string
  strategyName?: string
  pair: string
  timeframe?: string
  initialCapital?: number
  positionSize?: number | null
  status: string
  phase?: string
  startAt: string
  endAt: string
  createdAt: string
  completedAt: string | null
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
  enteredAt: string
  exitedAt: string
  entryPrice: number
  exitPrice: number
  quantity: number
  pnl: number
  pnlPercent: number
}

export interface BacktestKlineAnalysis {
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
  avgEntryPrice: number | null
  positionQuantity: number | null
  positionCost: number | null
  positionValue: number | null
  positionPnl: number | null
  positionPnlPercent: number | null
  taskPnl?: number | null
  taskPnlPercent?: number | null
}

export interface AnalysisResponse {
  total: number
  page: number
  pageSize: number
  totalPages: number
  items: BacktestKlineAnalysis[]
}

export const backtestsApi = {
  start(strategyId: string, exchangeId: string, pair: string, timeframe: string, startAt: string, endAt: string, initialCapital: number, positionSize?: number | null) {
    return client.post<{ taskId: string; status: string; createdAt: string; strategyName?: string; pair?: string; timeframe?: string }>('/backtests', {
      strategyId,
      exchangeId,
      pair,
      timeframe,
      startAt,
      endAt,
      initialCapital,
      positionSize: positionSize ?? null
    })
  },
  getTasks(strategyId?: string) {
    const url = strategyId ? `/backtests/tasks?strategyId=${strategyId}` : '/backtests/tasks'
    return client.get<BacktestTask[]>(url)
  },
  getTask(taskId: string) {
    return client.get<BacktestTask>(`/backtests/tasks/${taskId}`)
  },
  getResult(taskId: string) {
    return client.get<BacktestResult>(`/backtests/tasks/${taskId}/result`)
  },
  getAnalysis(taskId: string, page = 1, pageSize = 100, action?: string) {
    let url = `/backtests/tasks/${taskId}/analysis?page=${page}&pageSize=${pageSize}`
    if (action && action !== 'all') url += `&action=${encodeURIComponent(action)}`
    return client.get<AnalysisResponse>(url)
  }
}
