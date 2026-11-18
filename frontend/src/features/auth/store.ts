import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { ApiError } from '@/shared/api/http'
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
  }

  async function reload() {
    loaded.value = false
    await ensureLoaded()
  }

  async function signIn(email: string, password: string, token?: string | null) {
    await authApi.signIn(email, password, token)
    await reload()
  }

  /** Creates the account, then signs in with the same credentials. */
  async function register(input: RegisterInput, token?: string | null) {
    await authApi.register(input, token)
    await authApi.signIn(input.email, input.password)
    await reload()
  }

  async function demoSignIn(role: DemoRole) {
    await authApi.demoSignIn(role)
    await reload()
  }

  async function signOut() {
    try {
      await authApi.signOut()
    } finally {
      user.value = null
      loaded.value = true
    }
  }

  function patchUser(changes: Partial<Me>) {
    if (user.value) user.value = { ...user.value, ...changes }
  }

  return { user, isSignedIn, ensureLoaded, signIn, register, demoSignIn, signOut, patchUser }
})
