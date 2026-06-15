import client from './client'

export interface Strategy {
  id: string
  name: string
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
  executionRule?: string
}

export interface UpdateStrategyRequest {
  name?: string
  executionRule?: string
}

export interface StrategySchema {
  indicators: string[]
  contextIndicators: string[]
  comparisons: string[]
  groupOperators: string[]
  actions: string[]
  contexts: string[]
  sizeTypes: string[]
}

export interface ValidationResult {
  valid: boolean
  issues: { path: string; message: string }[]
}

export const strategiesApi = {
  /** 获取可用指标/比较符/操作符 schema */
  getSchema() {
    return client.get<StrategySchema>('/strategies/schema')
  },

  /** 校验执行规则集 */
  validateRuleSet(executionRule: string) {
    return client.post<ValidationResult>('/strategies/validate', { executionRule })
  },

  getAllPure() {
    return client.get<Strategy[]>('/strategies')
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
