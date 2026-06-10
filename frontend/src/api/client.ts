import axios from 'axios'

export const ErrorCode = {
  ValidationError: 1000,
  Unauthenticated: 1002,
  AuthMfaRequired: 1102,
  AuthMfaInvalidCode: 1103,
  AuthMfaNotConfigured: 1104,
  AuthMfaReplayDetected: 1105,
  AuthMfaSecretInvalid: 1106,
} as const

// Axios 请求配置扩展（运行时附加属性）
interface AxiosConfigMeta {
  _mfa?: boolean
  _retry?: boolean
}

// 拦截器错误类型（运行时附加属性）
interface InterceptorError {
  _mfaCancelled?: boolean
  response?: { data?: { code?: number }; status?: number }
  config: import('axios').AxiosRequestConfig & AxiosConfigMeta
  message?: string
}

/** 视图层 API 错误形状，用于替代 catch(err: any) */
export interface ApiError {
  _mfaCancelled?: boolean
  response?: { data?: { message?: string } }
}

const client = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' }
})

client.interceptors.request.use(config => {
  const token = localStorage.getItem('accessToken')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

let mfaModal: { requestCode: (error?: string) => Promise<string> } | null = null

export function registerMfaModal(modal: { requestCode: (error?: string) => Promise<string> }) {
  mfaModal = modal
}

client.interceptors.response.use(
  response => response,
  async (err: InterceptorError) => {
    const originalRequest = err.config
    const errCode = err.response?.data?.code

    // MFA 需要验证 或 验证码错误（重试）
    if ((errCode === ErrorCode.AuthMfaRequired
      || errCode === ErrorCode.AuthMfaInvalidCode
      || errCode === ErrorCode.AuthMfaReplayDetected)
      && !originalRequest._mfa && mfaModal) {
      originalRequest._mfa = true
      const code = await mfaModal.requestCode()
      if (!code) {
        err._mfaCancelled = true
        return Promise.reject(err)
      }
      if (!originalRequest.headers) originalRequest.headers = {}
      originalRequest.headers['X-MFA-Code'] = code
      return client(originalRequest)
    }

    // MFA 验证码错误 — 弹窗保留，显示错误提示让用户重新输入
    if (errCode === ErrorCode.AuthMfaInvalidCode && originalRequest._mfa && mfaModal) {
      const code = await mfaModal.requestCode('TOTP 验证码错误，请重新输入')
      if (!code) {
        err._mfaCancelled = true
        return Promise.reject(err)
      }
      if (!originalRequest.headers) originalRequest.headers = {}
      originalRequest.headers['X-MFA-Code'] = code
      return client(originalRequest)
    }

    // 401 且非 MFA（未取消）— 尝试 refresh token
    if (err.response?.status === 401 && !originalRequest._retry && !err._mfaCancelled) {
      originalRequest._retry = true
      const refreshToken = localStorage.getItem('refreshToken')
      if (refreshToken) {
        try {
          const { data } = await axios.post('/api/v1/auth/refresh', { refreshToken })
          localStorage.setItem('accessToken', data.accessToken)
          localStorage.setItem('refreshToken', data.refreshToken)
          if (!originalRequest.headers) originalRequest.headers = {}
      originalRequest.headers.Authorization = `Bearer ${data.accessToken}`
          return client(originalRequest)
        } catch {
          localStorage.clear()
          window.location.href = '/login'
        }
      }
    }
    return Promise.reject(err)
  }
)

export default client
