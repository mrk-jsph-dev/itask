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
}
