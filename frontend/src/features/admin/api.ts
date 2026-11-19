import { api } from '@/shared/api/http'

export interface AdminUser {
  id: string
  email: string
  displayName: string
  isPlatformAdmin: boolean
  isDemo: boolean
  isLocked: boolean
  createdAt: string
}

/** App-level account type. Board access still comes from board membership. */
export type UserType = 'User' | 'PlatformAdmin'

export const userTypes: { value: UserType; label: string }[] = [
  { value: 'User', label: 'User' },
  { value: 'PlatformAdmin', label: 'Platform admin' },
]

export interface Page<T> {
  items: T[]
  nextCursor: string | null
}

export const adminApi = {
  users: (cursor?: string | null) =>
    api.get<Page<AdminUser>>(
      cursor ? `/admin/users?cursor=${encodeURIComponent(cursor)}` : '/admin/users',
    ),
  setLocked: (userId: string, locked: boolean) =>
    api.put<void>(`/admin/users/${userId}/lock`, { locked }),
  setType: (userId: string, type: UserType) =>
    api.put<void>(`/admin/users/${userId}/type`, { type }),
}
