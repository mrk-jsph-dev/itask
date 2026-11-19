import { api } from '@/shared/api/http'
import type { ExternalProvider } from './external'

export interface Me {
  id: string
  email: string
  displayName: string
  avatarColor: string
  timeZone: string
  isPlatformAdmin: boolean
  isDemo: boolean
  /** False for accounts created through GitHub or Microsoft until a password is set. */
  hasPassword: boolean
  createdAt: string
}

export type DemoRole = 'admin' | 'owner' | 'editor' | 'viewer'

export interface RegisterInput {
  displayName: string
  email: string
  password: string
}

const turnstile = (token?: string | null) => (token ? { 'X-Turnstile-Token': token } : undefined)

export const authApi = {
  me: () => api.get<Me>('/me'),
  signIn: (email: string, password: string, token?: string | null) =>
    api.post<void>('/auth/login?useCookies=true', { email, password }, turnstile(token)),
  register: (input: RegisterInput, token?: string | null) =>
    api.post<void>('/auth/register', input, turnstile(token)),
  signOut: () => api.post<void>('/auth/logout'),
  /** Always succeeds, so it doesn't reveal which emails have accounts. */
  forgotPassword: (email: string, token?: string | null) =>
    api.post<void>('/auth/forgotPassword', { email }, turnstile(token)),
  resetPassword: (email: string, resetCode: string, newPassword: string) =>
    api.post<void>('/auth/resetPassword', { email, resetCode, newPassword }),
  demoStatus: () => api.get<{ enabled: boolean }>('/auth/demo'),
  demoSignIn: (role: DemoRole) => api.post<void>(`/auth/demo/${role}`),
  /** External providers configured on the server. */
  providers: () => api.get<{ providers: ExternalProvider[] }>('/auth/providers'),
  /** Redeems the code from the provider callback; `link` when it connected a provider to this account. */
  completeExternal: (code: string, nonce: string) =>
    api.post<{ mode: 'signIn' | 'link' }>('/auth/external/complete', { code, nonce }),
}
