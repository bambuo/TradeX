import client from './client'

export interface User {
  id: string
  username: string
  role: string
  status: string
  createdAt: string
}

export const usersApi = {
  getAll() {
    return client.get<{ data: User[]; total: number }>('/users')
  },
  create(data: { userName: string; password: string; role: string }) {
    return client.post<User>('/users', data)
  },
  update(id: string, data: { username: string; role: string }) {
    return client.put<{ message: string }>(`/users/${id}`, data)
  },
  delete(id: string) {
    return client.delete<{ message: string }>(`/users/${id}`)
  },
  updateRole(id: string, role: string) {
    return client.put<{ message: string }>(`/users/${id}/role`, { role })
  },
  updateStatus(id: string, status: string) {
    return client.put(`/users/${id}/status`, { status })
  }
}
