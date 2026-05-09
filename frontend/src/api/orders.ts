import client from './client'

export interface Order {
  id: string
  traderId: string
  exchangeOrderId: string | null
  exchangeId: string
  strategyId: string | null
  positionId: string | null
  Pair: string
  side: string
  type: string
  status: string
  price: number | null
  quantity: number
  filledQuantity: number
  quoteQuantity: number
  fee: number
  feeAsset: string | null
  isManual: boolean
  placedAtUtc: string
  updatedAt: string
}

export interface CreateManualOrderRequest {
  exchangeId: string
  Pair: string
  side: string
  type: string
  quantity: number
  price?: number
  strategyId?: string
}

export const ordersApi = {
  getAll(traderId: string) {
    return client.get<Order[]>(`/traders/${traderId}/orders`)
  },
  getById(traderId: string, id: string) {
    return client.get<Order>(`/traders/${traderId}/orders/${id}`)
  },
  createManual(traderId: string, data: CreateManualOrderRequest) {
    return client.post<Order>(`/traders/${traderId}/orders/manual`, data)
  }
}
