import { api } from '@/shared/api/http'

export interface Me {
  id: string
  email: string
  displayName: string
  isPlatformAdmin: boolean
  isDemo: boolean
}

export const authApi = {
  me: () => api.get<Me>('/me'),
  signIn: (email: string, password: string) =>
    api.post<void>('/auth/login?useCookies=true', { email, password }),
  register: (email: string, password: string) =>
    api.post<void>('/auth/register', { email, password }),
}
