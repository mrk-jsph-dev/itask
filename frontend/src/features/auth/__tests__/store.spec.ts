import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'
import { clearApiCache } from '@/shared/pwa/cache'
import { authApi, type Me } from '../api'
import { useAuthStore } from '../store'

vi.mock('@/shared/pwa/cache', () => ({ clearApiCache: vi.fn<() => Promise<void>>() }))

const me: Me = {
  id: '1',
  email: 'ada@example.com',
  displayName: 'Ada',
  avatarColor: '#1D5BF5',
  timeZone: 'UTC',
  isPlatformAdmin: false,
  isDemo: false,
  hasPassword: true,
  createdAt: '2026-11-18T00:00:00Z',
}

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('treats 401 from /me as signed out', async () => {
    vi.spyOn(authApi, 'me').mockRejectedValue(new ApiError(401, 'Unauthorized'))
    const store = useAuthStore()

    await store.ensureLoaded()

    expect(store.isSignedIn).toBe(false)
  })

  it('registers, then signs in with the same credentials', async () => {
    const register = vi.spyOn(authApi, 'register').mockResolvedValue()
    const signIn = vi.spyOn(authApi, 'signIn').mockResolvedValue()
    vi.spyOn(authApi, 'me').mockResolvedValue(me)
    const store = useAuthStore()

    await store.register({ displayName: 'Ada', email: me.email, password: 'pw' }, 'token')

    expect(register).toHaveBeenCalledWith(
      { displayName: 'Ada', email: me.email, password: 'pw' },
      'token',
    )
    expect(signIn).toHaveBeenCalledWith(me.email, 'pw')
    expect(store.user?.displayName).toBe('Ada')
  })

  it('signs in as a demo role', async () => {
    const demo = vi.spyOn(authApi, 'demoSignIn').mockResolvedValue()
    vi.spyOn(authApi, 'me').mockResolvedValue({ ...me, isDemo: true })
    const store = useAuthStore()

    await store.demoSignIn('editor')

    expect(demo).toHaveBeenCalledWith('editor')
    expect(store.user?.isDemo).toBe(true)
  })

  it('clears the user on sign out even if the request fails', async () => {
    vi.spyOn(authApi, 'me').mockResolvedValue(me)
    vi.spyOn(authApi, 'signOut').mockRejectedValue(new ApiError(500, 'Boom'))
    const store = useAuthStore()
    await store.ensureLoaded()

    await expect(store.signOut()).rejects.toThrow('Boom')

    expect(store.isSignedIn).toBe(false)
  })

  it('patches the signed-in user', async () => {
    vi.spyOn(authApi, 'me').mockResolvedValue(me)
    const store = useAuthStore()
    await store.ensureLoaded()

    store.patchUser({ displayName: 'Ada L.' })

    expect(store.user?.displayName).toBe('Ada L.')
  })

  it('completes an external sign-in and loads the user', async () => {
    const complete = vi.spyOn(authApi, 'completeExternal').mockResolvedValue({ mode: 'signIn' })
    vi.spyOn(authApi, 'me').mockResolvedValue(me)
    const store = useAuthStore()

    const mode = await store.completeExternal('code', 'nonce')

    expect(complete).toHaveBeenCalledWith('code', 'nonce')
    expect(mode).toBe('signIn')
    expect(store.user).toEqual(me)
  })

  it('drops the offline cache on sign-out, even when the request fails', async () => {
    vi.spyOn(authApi, 'signOut').mockRejectedValue(new TypeError('Failed to fetch'))
    const store = useAuthStore()

    await expect(store.signOut()).rejects.toThrow('Failed to fetch')

    expect(clearApiCache).toHaveBeenCalled()
    expect(store.isSignedIn).toBe(false)
  })

  it('drops the offline cache when a different person signs in', async () => {
    vi.spyOn(authApi, 'signIn').mockResolvedValue()
    const getMe = vi.spyOn(authApi, 'me').mockResolvedValue(me)
    const store = useAuthStore()
    await store.ensureLoaded()
    vi.mocked(clearApiCache).mockClear()

    await store.signIn(me.email, 'pw')
    expect(clearApiCache).not.toHaveBeenCalled()

    getMe.mockResolvedValue({ ...me, id: '2' })
    await store.signIn('grace@example.com', 'pw')
    expect(clearApiCache).toHaveBeenCalledOnce()
  })
})
