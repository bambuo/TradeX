import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '../api/auth'

export interface AuthUser {
  id: string
  username: string
  email: string
  role: string
  isMfaEnabled: boolean
}

export const useAuthStore = defineStore('auth', () => {
  const user = ref<AuthUser | null>(null)
  const mfaToken = ref<string | null>(null)
  const isAuthenticated = computed(() => !!user.value)
  const needsMfa = computed(() => !!mfaToken.value)

  async function login(username: string, password: string) {
    const { data: body } = await authApi.login({ username, password })
    if (body.mfaRequired) {
      mfaToken.value = body.mfaToken ?? null
      return { mfaRequired: true }
    }
    if (body.mfaSetupRequired) {
      mfaToken.value = body.mfaToken ?? null
      return { mfaSetupRequired: true, mfaToken: body.mfaToken, expiresIn: body.expiresIn }
    }
    localStorage.setItem('accessToken', body.accessToken)
    localStorage.setItem('refreshToken', body.refreshToken)
    loadFromStorage()
    return { mfaRequired: false }
  }

  async function verifyMfa(totpCode: string) {
    if (!mfaToken.value) throw new Error('No MFA token')
    const { data: body } = await authApi.verifyMfa({ mfaToken: mfaToken.value, totpCode })
    localStorage.setItem('accessToken', body.accessToken)
    localStorage.setItem('refreshToken', body.refreshToken)
    mfaToken.value = null
    loadFromStorage()
    return body
  }

  async function verifyMfaWithRecoveryCode(recoveryCode: string) {
    if (!mfaToken.value) throw new Error('No MFA token')
    const { data: body } = await authApi.verifyMfa({ mfaToken: mfaToken.value, recoveryCode })
    localStorage.setItem('accessToken', body.accessToken)
    localStorage.setItem('refreshToken', body.refreshToken)
    mfaToken.value = null
    loadFromStorage()
    return body
  }

  async function register(username: string, email: string, password: string) {
    await authApi.register({ username, email, password })
  }

  async function logout() {
    try {
      await authApi.logout()
    } finally {
      localStorage.clear()
      user.value = null
      mfaToken.value = null
    }
  }

  function loadFromStorage() {
    const token = localStorage.getItem('accessToken')
    if (token) {
      try {
        const payload = JSON.parse(base64UrlDecode(token.split('.')[1]))
        user.value = {
          id: payload.nameidentifier || payload.sub,
          username: payload.name || payload.unique_name,
          role: payload.role || 'Viewer',
          email: '',
          isMfaEnabled: payload.mfa === true || payload.mfa === 'true'
        }
      } catch {
        localStorage.clear()
      }
    }
  }

  function base64UrlDecode(str: string): string {
    str = str.replace(/-/g, '+').replace(/_/g, '/')
    while (str.length % 4) str += '='
    return atob(str)
  }

  return { user, mfaToken, isAuthenticated, needsMfa, login, verifyMfa, verifyMfaWithRecoveryCode, register, logout, loadFromStorage }
})
