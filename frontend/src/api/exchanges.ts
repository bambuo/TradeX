import client from './client'

export interface Exchange {
  id: string
  name: string
  type: string
  status: string
  isTestnet: boolean
  lastTestedAt: string | null
  testResult: string | null
  createdAt: string
  updatedAt: string | null
}

export interface ExchangeOrder {
  pair: string
  side: string
  type: string
  status: string
  price: number
  quantity: number
  filledQuantity: number
  exchangeOrderId: string
  placedAt: string
}

export interface CreateExchangeRequest {
  name: string
  exchangeType: string
  apiKey: string
  secretKey: string
  passphrase?: string
  isTestnet?: boolean
}

export interface UpdateExchangeRequest {
  name?: string
  apiKey?: string
  secretKey?: string
  passphrase?: string
}

export interface PagedResult<T> {
  data: T[]
  total: number
  page: number
  pageSize: number
}

export const exchangesApi = {
  getAll() {
    return client.get<Exchange[]>('/exchanges')
  },
  create(data: CreateExchangeRequest) {
    return client.post<Exchange>('/exchanges', data)
  },
  update(id: string, data: UpdateExchangeRequest) {
    return client.put<Exchange>(`/exchanges/${id}`, data)
  },
  delete(id: string) {
    return client.delete(`/exchanges/${id}`)
  },
  testConnection(id: string) {
    return client.post<{ connected: boolean; error?: string }>(`/exchanges/${id}/test`)
  },
  toggleStatus(id: string, enable: boolean) {
    return client.post<void>(`/exchanges/${id}/toggle`, { enable })
  },
  getPairs(id: string) {
    return client.get<{ pair: string; pricePrecision: number; quantityPrecision: number; minNotional: number; price: number; priceChangePercent: number; volume: number; highPrice: number; lowPrice: number }[]>(`/exchanges/${id}/pairs`)
  },
  getAssets(id: string) {
    return client.get<{ currency: string; balance: number }[]>(`/exchanges/${id}/assets`)
  },
  getOrders(id: string, type: 'open' | 'history' = 'open', page = 1, pageSize = 10, pair?: string, side?: string, orderType?: string, status?: string) {
    let url = `/exchanges/${id}/orders?type=${type}&page=${page}&pageSize=${pageSize}`
    if (pair) url += `&pair=${encodeURIComponent(pair)}`
    if (side) url += `&side=${encodeURIComponent(side)}`
    if (orderType) url += `&orderType=${encodeURIComponent(orderType)}`
    if (status) url += `&status=${encodeURIComponent(status)}`
    return client.get<PagedResult<ExchangeOrder>>(url)
  }
}
