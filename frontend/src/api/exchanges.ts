import client from './client'

export interface ExchangeAccount {
  id: string
  trader_id?: string
  exchangeType: string
  label: string
  isTestnet: boolean
  isEnabled: boolean
  createdAt: string
  updatedAt: string | null
  traderName: string
  lastTestedAtUtc: string | null
  testResult: string | null
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
    return client.get<{ data: ExchangeAccount[] }>('/exchanges')
  },
  create(data: CreateExchangeRequest) {
    return client.post<ExchangeAccount>('/exchanges', data)
  },
  update(id: string, data: UpdateExchangeRequest) {
    return client.put<ExchangeAccount>(`/exchanges/${id}`, data)
  },
  delete(id: string) {
    return client.delete(`/exchanges/${id}`)
  },
  testConnection(id: string) {
    return client.post<{ connected: boolean; error?: string }>(`/exchanges/${id}/test`)
  },
  getSymbols(id: string) {
    return client.get<{ data: { symbol: string; pricePrecision: number; quantityPrecision: number; minNotional: number; price: number; priceChangePercent: number; volume: number; highPrice: number; lowPrice: number }[] }>(`/exchanges/${id}/symbols`)
  }
}
