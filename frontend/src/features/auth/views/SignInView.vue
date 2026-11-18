<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import { ApiError } from '@/shared/api/http'
import { useAuthStore } from '../store'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

async function submit() {
  busy.value = true
  error.value = null
  try {
    await auth.signIn(email.value, password.value)
    const next =
      typeof route.query.next === 'string' && route.query.next.startsWith('/')
        ? route.query.next
        : '/'
    await router.replace(next)
  } catch (e) {
    error.value =
      e instanceof ApiError && e.status === 401
        ? 'Email or password is incorrect.'
        : 'Something went wrong.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center px-4 py-12">
    <div
      class="fade-up flex w-full max-w-[460px] flex-col gap-6 rounded-[var(--radius-panel)] bg-panel p-10"
    >
      <div class="flex items-center gap-2.5">
        <AppLogo />
        <span class="text-xl font-bold">iTask</span>
      </div>
      <div class="flex flex-col gap-2">
        <h1 class="text-[28px] font-bold tracking-tight">Welcome back</h1>
        <p class="text-[15px] text-muted">Sign in to your boards.</p>
      </div>
      <form class="flex flex-col gap-4" @submit.prevent="submit">
        <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
          Email
          <input
            v-model="email"
            type="email"
            autocomplete="email"
            required
            class="h-[46px] rounded-[var(--radius-control)] border border-line px-3.5 text-[15px] font-normal"
          />
        </label>
        <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
          Password
          <input
            v-model="password"
            type="password"
            autocomplete="current-password"
            required
            class="h-[46px] rounded-[var(--radius-control)] border border-line px-3.5 text-[15px] font-normal"
          />
        </label>
        <p v-if="error" role="alert" class="text-sm text-red-600">{{ error }}</p>
        <button
          type="submit"
          :disabled="busy"
          class="press mt-1 h-12 rounded-full bg-accent text-[15px] font-semibold text-white disabled:opacity-60"
        >
          {{ busy ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>
    </div>
  </div>
</template>
