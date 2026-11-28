import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { ApiError } from '@/shared/api/http'
import { clearApiCache } from '@/shared/pwa/cache'
import { useThemeStore } from '@/shared/theme/store'
import { authApi, type DemoRole, type Me, type RegisterInput } from './api'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<Me | null>(null)
  const loaded = ref(false)

  const isSignedIn = computed(() => user.value !== null)

  async function ensureLoaded() {
    if (loaded.value) return
    try {
      user.value = await authApi.me()
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 401) throw error
      user.value = null
    } finally {
      loaded.value = true
    }
    if (user.value) useThemeStore().adopt(user.value.themePreference, !user.value.isDemo)
    else useThemeStore().detach()
  }

  async function reload() {
    const previous = user.value?.id
    loaded.value = false
    await ensureLoaded()
    // A (possibly different) person just signed in: drop what was cached for offline reading.
    if (user.value?.id !== previous) await clearApiCache()
  }

  async function signIn(email: string, password: string, token?: string | null) {
    await authApi.signIn(email, password, token)
    await reload()
  }

  /** Creates the account; it can sign in once `verifyEmail` confirms the emailed code. */
  async function register(input: RegisterInput, token?: string | null) {
    return (await authApi.register(input, token)).email
  }

  /** Confirms the sign-up code; the API signs the new account in. */
  async function verifyEmail(email: string, code: string) {
    await authApi.verifyEmail(email, code)
    await reload()
  }

  async function demoSignIn(role: DemoRole) {
    await authApi.demoSignIn(role)
    await reload()
  }

  /** Finishes a GitHub or Microsoft sign-in (or link) and reloads the signed-in user. */
  async function completeExternal(code: string, nonce: string) {
    const { mode } = await authApi.completeExternal(code, nonce)
    await reload()
    return mode
  }

  async function signOut() {
    try {
      await authApi.signOut()
    } finally {
      user.value = null
      loaded.value = true
      await clearApiCache()
    }
  }

  function patchUser(changes: Partial<Me>) {
    if (user.value) user.value = { ...user.value, ...changes }
  }

  return {
    user,
    isSignedIn,
    ensureLoaded,
    signIn,
    register,
    verifyEmail,
    demoSignIn,
    completeExternal,
    signOut,
    patchUser,
  }
})
