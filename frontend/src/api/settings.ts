import client from './client'

export interface Setting {
  key: string
  value: string
}

export const settingsApi = {
  getAll() {
    return client.get<{ data: Setting[] }>('/settings')
  },
  update(settings: { key: string; value: string }[]) {
    return client.put<{ message: string }>('/settings', { settings })
  }
}
