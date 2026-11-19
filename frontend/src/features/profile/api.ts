import { api } from '@/shared/api/http'
import type { ExternalProvider } from '@/features/auth/external'

export interface ProfileInput {
  displayName: string
  timeZone: string
  avatarColor: string
}

/** Counts on the profile card: boards, cards assigned to me, and those completed. */
export interface MyStats {
  boards: number
  tasks: number
  done: number
}

/** Sign-in methods on the account and the providers the server offers. */
export interface MyLogins {
  hasPassword: boolean
  logins: { provider: ExternalProvider; accountName: string | null }[]
  providers: ExternalProvider[]
}

export const profileApi = {
  stats: () => api.get<MyStats>('/me/stats'),
  update: (input: ProfileInput) => api.put<ProfileInput>('/me', input),
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post<void>('/me/password', { currentPassword, newPassword }),
  setPassword: (newPassword: string) => api.post<void>('/me/password/set', { newPassword }),
  logins: () => api.get<MyLogins>('/me/logins'),
  /** Ticket for `/api/auth/external/{provider}?link=`, valid for 5 minutes. */
  startLink: (provider: ExternalProvider) =>
    api.post<{ ticket: string }>(`/me/logins/${provider}/link`),
  removeLogin: (provider: ExternalProvider) => api.del<void>(`/me/logins/${provider}`),
}
