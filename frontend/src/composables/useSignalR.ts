import { ref } from 'vue'
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'

export function useSignalR() {
  const connection = ref<HubConnection | null>(null)
  const connected = ref(false)

  function getBaseUrl() {
    return import.meta.env.VITE_API_BASE_URL ?? ''
  }

  async function connect(traderId?: string) {
    if (connection.value?.state === HubConnectionState.Connected) return

    const token = localStorage.getItem('accessToken')
    if (!token) return

    const hub = new HubConnectionBuilder()
      .withUrl(`${getBaseUrl()}/hubs/trading`, {
        accessTokenFactory: () => token
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build()

    hub.onreconnecting(() => { connected.value = false })
    hub.onreconnected(() => { connected.value = true })
    hub.onclose(() => { connected.value = false })

    await hub.start()
    connection.value = hub
    connected.value = true

    if (traderId) {
      await hub.invoke('JoinTraderGroup', traderId)
    }
  }

  async function disconnect() {
    if (connection.value) {
      await connection.value.stop()
      connection.value = null
      connected.value = false
    }
  }

  function on<T>(event: string, handler: (data: T) => void) {
    connection.value?.on(event, handler)
  }

  function off(event: string) {
    connection.value?.off(event)
  }

  return { connection, connected, connect, disconnect, on, off }
}
