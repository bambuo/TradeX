import client from './client'

export interface Trader {
  id: string
  name: string
  status: string
  createdAt: string
  updatedAt: string | null
}

export const tradersApi = {
  getAll() {
    return client.get<Trader[]>('/traders')
  },
  getById(id: string) {
    return client.get<Trader>(`/traders/${id}`)
  },
  create(name: string) {
    return client.post<Trader>('/traders', { name })
  },
  update(id: string, name: string) {
    return client.put<Trader>(`/traders/${id}`, { name })
  },
  delete(id: string) {
    return client.delete<{ message: string }>(`/traders/${id}`)
  }
}
