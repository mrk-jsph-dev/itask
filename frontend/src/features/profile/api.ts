import { api } from '@/shared/api/http'

export interface ProfileInput {
  displayName: string
  timeZone: string
  avatarColor: string
}

export const profileApi = {
  update: (input: ProfileInput) => api.put<ProfileInput>('/me', input),
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post<void>('/me/password', { currentPassword, newPassword }),
}
