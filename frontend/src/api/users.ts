import client from './client'

export interface User {
  id: string
  userName: string
  role: string
  status: string
  createdAtUtc: string
}

export const usersApi = {
  getAll() {
    return client.get<{ data: User[]; total: number }>('/users')
  },
  create(data: { userName: string; password: string; role: string }) {
    return client.post<User>('/users', data)
  },
  updateRole(id: string, role: string) {
    return client.put<{ message: string }>(`/users/${id}/role`, { role })
  },
  updateStatus(id: string, status: string) {
    return client.put(`/users/${id}/status`, { status })
  }
}
