import client from './client'

export interface Position {
  id: string
  traderId: string
  exchangeId: string
  strategyId: string
  Pair: string
  quantity: number
  entryPrice: number
  currentPrice: number
  unrealizedPnl: number
  realizedPnl: number
  status: string
  openedAtUtc: string
  closedAtUtc: string | null
  updatedAt: string
}

export const positionsApi = {
  getAll(traderId: string, openOnly?: boolean) {
    const params = openOnly ? { openOnly: 'true' } : undefined
    return client.get<Position[]>(`/traders/${traderId}/positions`, { params })
  },
  getById(traderId: string, id: string) {
    return client.get<Position>(`/traders/${traderId}/positions/${id}`)
  }
}
