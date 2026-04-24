import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi, type AuthResponse } from '../api/auth'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<AuthResponse['user'] | null>(null)
  const mfaToken = ref<string | null>(null)
  const isAuthenticated = computed(() => !!user.value)
  const needsMfa = computed(() => !!mfaToken.value)

  async function login(username: string, password: string) {
    const { data } = await authApi.login({ username, password })
    if (data.mfaRequired) {
      mfaToken.value = data.mfaToken ?? null
      return { mfaRequired: true }
    }
    if (data.mfaSetupRequired) {
      mfaToken.value = data.mfaToken ?? null
      return { mfaSetupRequired: true, mfaToken: data.mfaToken, expiresIn: data.expiresIn }
    }
    return { mfaRequired: false }
  }

  async function verifyMfa(totpCode: string) {
    if (!mfaToken.value) throw new Error('No MFA token')
    const { data } = await authApi.verifyMfa({ mfaToken: mfaToken.value, totpCode })
    localStorage.setItem('accessToken', data.accessToken)
    localStorage.setItem('refreshToken', data.refreshToken)
    mfaToken.value = null
    user.value = { id: '', username: '', role: data.role, email: '', isMfaEnabled: true }
    return data
  }

  async function verifyMfaWithRecoveryCode(recoveryCode: string) {
    if (!mfaToken.value) throw new Error('No MFA token')
    const { data } = await authApi.verifyMfa({ mfaToken: mfaToken.value, recoveryCode })
    localStorage.setItem('accessToken', data.accessToken)
    localStorage.setItem('refreshToken', data.refreshToken)
    mfaToken.value = null
    user.value = { id: '', username: '', role: data.role, email: '', isMfaEnabled: true }
    return data
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
        const payload = JSON.parse(atob(token.split('.')[1]))
        user.value = {
          id: payload.nameidentifier || payload.sub,
          username: payload.name || payload.unique_name,
          role: payload.role || 'Viewer',
          email: '',
          isMfaEnabled: payload.mfa === 'true'
        }
      } catch {
        localStorage.clear()
      }
    }
  }

  return { user, mfaToken, isAuthenticated, needsMfa, login, verifyMfa, verifyMfaWithRecoveryCode, register, logout, loadFromStorage }
})
