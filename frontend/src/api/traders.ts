import client from './client'

export interface Trader {
  id: string
  name: string
  status: string
  avatarColor?: string
  avatarUrl?: string
  style?: string
  createdAt: string
  updatedAt: string | null
}

export interface TraderStats {
  totalTrades: number
  winRate: number
  profitLossRatio: number
  sharpeRatio: number
}

export const tradersApi = {
  getAll() {
    return client.get<Trader[]>('/traders')
  },
  getById(id: string) {
    return client.get<Trader>(`/traders/${id}`)
  },
  create(data: { name: string; avatarColor?: string }) {
    return client.post<Trader>('/traders', data)
  },
  update(id: string, data: { name?: string; status?: string; avatarColor?: string }) {
    return client.put<Trader>(`/traders/${id}`, data)
  },
  delete(id: string) {
    return client.delete<{ message: string }>(`/traders/${id}`)
  },
  getStats(id: string) {
    return client.get<TraderStats>(`/traders/${id}/stats`)
  },
  uploadAvatar(id: string, file: File) {
    const form = new FormData()
    form.append('file', file)
    return client.post<{ avatarUrl: string }>(`/traders/${id}/avatar`, form, {
      headers: { 'Content-Type': 'multipart/form-data' }
    })
  }
}
