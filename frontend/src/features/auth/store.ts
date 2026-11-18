import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { ApiError } from '@/shared/api/http'
import { authApi, type Me } from './api'

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

  async function signIn(email: string, password: string) {
    await authApi.signIn(email, password)
    loaded.value = false
    await ensureLoaded()
  }

  return { user, isSignedIn, ensureLoaded, signIn }
})
