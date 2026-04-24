import client from './client'

export interface LoginRequest {
  username: string
  password: string
}

export interface LoginResponse {
  mfaRequired: boolean
  mfaToken?: string
  mfaSetupRequired?: boolean
  userId?: string
  expiresIn?: number
}

export interface VerifyMfaRequest {
  mfaToken: string
  totpCode?: string
  recoveryCode?: string
}

export interface VerifyMfaResponse {
  accessToken: string
  refreshToken: string
  expiresIn: number
  role: string
  recoveryCodes?: string[]
}

export interface RegisterRequest {
  username: string
  email: string
  password: string
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  user: {
    id: string
    username: string
    email: string
    role: string
    isMfaEnabled: boolean
  }
}

export const authApi = {
  login(data: LoginRequest) {
    return client.post<LoginResponse>('/auth/login', data)
  },
  verifyMfa(data: VerifyMfaRequest) {
    return client.post<VerifyMfaResponse>('/auth/verify-mfa', data)
  },
  register(data: RegisterRequest) {
    return client.post<{ message: string; userId: string }>('/auth/register', data)
  },
  refresh(refreshToken: string) {
    return client.post<{ accessToken: string; refreshToken: string; expiresIn: number }>('/auth/refresh', { refreshToken })
  },
  logout() {
    return client.post<{ message: string }>('/auth/logout')
  },
  setupMfa() {
    return client.post<{ secretKey: string; qrCodeUrl: string; qrCodeImage: string; recoveryCodes: string[] }>('/auth/mfa/setup')
  },
  verifyMfaSetup(code: string) {
    return client.post<{ recoveryCodes: string[]; accessToken: string; refreshToken: string; expiresIn: number }>('/auth/mfa/verify', { code })
  },
  sendRecoveryCodes(userId: string) {
    return client.post<{ recoveryCodes: string[] }>('/auth/send-recovery-codes', { userId })
  }
}
