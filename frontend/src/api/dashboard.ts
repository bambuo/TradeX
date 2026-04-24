import client from './client'

export interface DashboardStats {
  traderCount: number
  strategyCount: number
  activeStrategyCount: number
  openPositionCount: number
  todayOrderCount: number
}

export const dashboardApi = {
  getStats() {
    return client.get<DashboardStats>('/dashboard/stats')
  }
}
