import client from './client'

export interface AuditLogEntry {
  id: string
  userId: string
  action: string
  resource: string
  resourceId: string | null
  detail: string | null
  ipAddress: string
  timestamp: string
}

export interface AuditLogResponse {
  data: AuditLogEntry[]
  total: number
}

export const auditLogApi = {
  getAll(params: {
    page?: number
    pageSize?: number
    userId?: string
    action?: string
    resourceType?: string
    startUtc?: string
    endUtc?: string
  } = {}) {
    return client.get<AuditLogResponse>('/audit-logs', { params })
  }
}
