import client from './client'

export interface Strategy {
  id: string
  traderId: string
  name: string
  exchangeId: string
  symbolIds: string
  timeframe: string
  entryConditionJson: string
  exitConditionJson: string
  executionRuleJson: string
  status: string
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateStrategyRequest {
  name: string
  exchangeId: string
  symbolIds?: string
  timeframe?: string
  entryConditionJson?: string
  exitConditionJson?: string
  executionRuleJson?: string
}

export interface UpdateStrategyRequest {
  name?: string
  symbolIds?: string
  timeframe?: string
  entryConditionJson?: string
  exitConditionJson?: string
  executionRuleJson?: string
}

export const strategiesApi = {
  getAll(traderId: string) {
    return client.get<Strategy[]>(`/traders/${traderId}/strategies`)
  },
  getById(traderId: string, id: string) {
    return client.get<Strategy>(`/traders/${traderId}/strategies/${id}`)
  },
  create(traderId: string, data: CreateStrategyRequest) {
    return client.post<Strategy>(`/traders/${traderId}/strategies`, data)
  },
  update(traderId: string, id: string, data: UpdateStrategyRequest) {
    return client.put<Strategy>(`/traders/${traderId}/strategies/${id}`, data)
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
