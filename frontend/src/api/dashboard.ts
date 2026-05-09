import client from './client'

export interface DashboardStats {
  traderCount: number
  strategyCount: number
  activeStrategyCount: number
  openPositionCount: number
  todayOrderCount: number
  totalBalance: number
  totalPnl: number
  totalPnlPercent: number
  dailyLossPercent: number
  maxDrawdownPercent: number
  riskStatus: string
  exchangeStatus: Record<string, string | null>
  recentTrades: DashboardTrade[]
}

export interface DashboardTrade {
  orderId: string
  pair: string
  side: string
  quantity: number
  price: number
  placedAtUtc: string
}

export const dashboardApi = {
  getStats() {
    return client.get<DashboardStats>('/dashboard/stats')
  }
}
