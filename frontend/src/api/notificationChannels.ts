import client from './client'

export interface NotificationChannel {
  id: string
  name: string
  type: string
  status: string
  isDefault: boolean
  lastTestedAtUtc: string | null
  createdAtUtc: string
}

export const notificationChannelsApi = {
  getAll() {
    return client.get<{ data: NotificationChannel[] }>('/notifications/channels')
  },
  create(data: { name: string; type: string; config: Record<string, string>; isDefault?: boolean }) {
    return client.post<NotificationChannel>('/notifications/channels', data)
  },
  delete(id: string) {
    return client.delete<{ message: string }>(`/notifications/channels/${id}`)
  },
  test(id: string) {
    return client.post<{ success: boolean; message: string }>(`/notifications/channels/${id}/test`)
  }
}
