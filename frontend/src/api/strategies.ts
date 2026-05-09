import client from './client'

export interface Strategy {
  id: string
  name: string
  entryCondition: string
  exitCondition: string
  executionRule: string
  version: number
  createdAt: string
  updatedAt: string
}

export interface StrategyBinding {
  id: string
  strategyId: string
  name?: string
  traderId: string
  exchangeId: string
  pairs: string
  timeframe: string
  status: string
  scope: string
  createdAt: string
  updatedAt: string
}

export interface GlobalBinding extends StrategyBinding {
  traderName: string
}

export interface CreateBindingRequest {
  strategyId: string
  exchangeId: string
  pairs?: string
  timeframe?: string
}

export interface UpdateBindingRequest {
  pairs?: string
  timeframe?: string
}

export interface CreateStrategyRequest {
  name: string
  entryCondition?: string
  exitCondition?: string
  executionRule?: string
}

export interface UpdateStrategyRequest {
  name?: string
  entryCondition?: string
  exitCondition?: string
  executionRule?: string
}

export const strategiesApi = {
  getAllPure() {
    return client.get<{ data: Strategy[] }>('/strategies')
  },
  getPureById(id: string) {
    return client.get<Strategy>(`/strategies/${id}`)
  },
  createPure(data: CreateStrategyRequest) {
    return client.post<Strategy>('/strategies', data)
  },
  updatePure(id: string, data: UpdateStrategyRequest) {
    return client.put<Strategy>(`/strategies/${id}`, data)
  },
  deletePure(id: string) {
    return client.delete(`/strategies/${id}`)
  },

  getAll(traderId: string) {
    return client.get<StrategyBinding[]>(`/traders/${traderId}/strategies`)
  },
  getById(traderId: string, id: string) {
    return client.get<StrategyBinding>(`/traders/${traderId}/strategies/${id}`)
  },
  create(traderId: string, data: CreateBindingRequest) {
    return client.post<StrategyBinding>(`/traders/${traderId}/strategies`, data)
  },
  update(traderId: string, id: string, data: UpdateBindingRequest) {
    return client.put<StrategyBinding>(`/traders/${traderId}/strategies/${id}`, data)
  },
  delete(traderId: string, id: string) {
    return client.delete<{ message: string }>(`/traders/${traderId}/strategies/${id}`)
  },
  toggle(traderId: string, id: string, enable: boolean) {
    return client.post<{ id: string; status: string; updatedAt: string }>(
      `/traders/${traderId}/strategies/${id}/toggle`, { enable }
    )
  }
}
