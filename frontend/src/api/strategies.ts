import client from './client'

export interface Strategy {
  id: string
  name: string
  entryConditionJson: string
  exitConditionJson: string
  executionRuleJson: string
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface StrategyDeployment {
  id: string
  strategyId: string
  traderId: string
  exchangeId: string
  symbolIds: string
  timeframe: string
  status: string
  scope: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface GlobalDeployment extends StrategyDeployment {
  traderName: string
}

export interface CreateDeploymentRequest {
  strategyId: string
  exchangeId: string
  symbolIds?: string
  timeframe?: string
}

export interface UpdateDeploymentRequest {
  symbolIds?: string
  timeframe?: string
}

export interface CreateStrategyRequest {
  name: string
  entryConditionJson?: string
  exitConditionJson?: string
  executionRuleJson?: string
}

export interface UpdateStrategyRequest {
  name?: string
  entryConditionJson?: string
  exitConditionJson?: string
  executionRuleJson?: string
}

export const strategiesApi = {
  // Pure strategy template API
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

  // Strategy deployment (per-trader) API
  getAll(traderId: string) {
    return client.get<StrategyDeployment[]>(`/traders/${traderId}/strategies`)
  },
  getById(traderId: string, id: string) {
    return client.get<StrategyDeployment>(`/traders/${traderId}/strategies/${id}`)
  },
  create(traderId: string, data: CreateDeploymentRequest) {
    return client.post<StrategyDeployment>(`/traders/${traderId}/strategies`, data)
  },
  update(traderId: string, id: string, data: UpdateDeploymentRequest) {
    return client.put<StrategyDeployment>(`/traders/${traderId}/strategies/${id}`, data)
  },
  delete(traderId: string, id: string) {
    return client.delete<{ message: string }>(`/traders/${traderId}/strategies/${id}`)
  },
  toggle(traderId: string, id: string, enable: boolean) {
    return client.post<{ id: string; status: string; updatedAtUtc: string }>(
      `/traders/${traderId}/strategies/${id}/toggle`, { enable }
    )
  }
}
