import client from './client'

export interface ExchangeAccount {
  id: string
  traderId: string
  exchangeType: string
  label: string
  isTestnet: boolean
  isEnabled: boolean
  createdAt: string
  updatedAt: string | null
}

export interface CreateExchangeAccountRequest {
  label: string
  exchangeType: string
  apiKey: string
  secretKey: string
  passphrase?: string
  isTestnet?: boolean
}

export interface UpdateExchangeAccountRequest {
  label?: string
  apiKey?: string
  secretKey?: string
  passphrase?: string
  isTestnet?: boolean
  isEnabled?: boolean
}

export const exchangeAccountsApi = {
  getAll(traderId: string) {
    return client.get<ExchangeAccount[]>(`/traders/${traderId}/exchanges`)
  },
  getById(traderId: string, id: string) {
    return client.get<ExchangeAccount>(`/traders/${traderId}/exchanges/${id}`)
  },
  create(traderId: string, data: CreateExchangeAccountRequest) {
    return client.post<ExchangeAccount>(`/traders/${traderId}/exchanges`, data)
  },
  update(traderId: string, id: string, data: UpdateExchangeAccountRequest) {
    return client.put<ExchangeAccount>(`/traders/${traderId}/exchanges/${id}`, data)
  },
  delete(traderId: string, id: string) {
    return client.delete<{ message: string }>(`/traders/${traderId}/exchanges/${id}`)
  },
  testConnection(traderId: string, id: string) {
    return client.post<{ connected: boolean; error?: string }>(`/traders/${traderId}/exchanges/${id}/test`)
  }
}
