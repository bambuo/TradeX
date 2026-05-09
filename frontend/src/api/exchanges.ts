import client from './client'

export interface Exchange {
  id: string
  traderId?: string
  exchangeType: string
  label: string
  isTestnet: boolean
  isEnabled: boolean
  createdAt: string
  updatedAt: string | null
  traderName: string
  lastTestedAt: string | null
  testResult: string | null
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

export const exchangesApi = {
  getAll() {
    return client.get<{ data: Exchange[] }>('/exchanges')
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
    return client.post<{ id: string; isEnabled: boolean }>(`/exchanges/${id}/toggle`, { enable })
  },
  getPairs(id: string) {
    return client.get<{ data: { pair: string; pricePrecision: number; quantityPrecision: number; minNotional: number; price: number; priceChangePercent: number; volume: number; highPrice: number; lowPrice: number }[] }>(`/exchanges/${id}/pairs`)
  },
  getAssets(id: string) {
    return client.get<{ data: { currency: string; balance: number }[] }>(`/exchanges/${id}/assets`)
  },
  getOrders(id: string, type: 'open' | 'history' = 'open') {
    return client.get<{ data: ExchangeOrder[] }>(`/exchanges/${id}/orders?type=${type}`)
  }
}
