import { api } from '@/shared/api/http'

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

export const profileApi = {
  stats: () => api.get<MyStats>('/me/stats'),
  update: (input: ProfileInput) => api.put<ProfileInput>('/me', input),
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post<void>('/me/password', { currentPassword, newPassword }),
}
