<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

import { ApiError } from '@/shared/api/http'
import AppLogo from '@/shared/ui/AppLogo.vue'
import { authApi } from '../api'
import { passwordStrength } from '../passwordStrength'

const route = useRoute()
const email = ref(typeof route.query.email === 'string' ? route.query.email : '')
const code = ref(typeof route.query.code === 'string' ? route.query.code : '')
const password = ref('')
const busy = ref(false)
const done = ref(false)
const error = ref<string | null>(null)
const strength = computed(() => passwordStrength(password.value))

async function submit() {
  busy.value = true
  error.value = null
  try {
    await authApi.resetPassword(email.value.trim(), code.value.trim(), password.value)
    done.value = true
  } catch (e) {
    error.value =
      e instanceof ApiError && e.status === 400
        ? 'This reset link is invalid or has expired. Ask for a new one.'
        : 'Something went wrong.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center p-4">
    <div
      class="fade-up flex w-full max-w-[440px] flex-col gap-6 rounded-[28px] bg-panel p-8 shadow-[0_1px_2px_rgb(17_24_39/0.04)]"
    >
      <div class="flex items-center gap-2.5">
        <AppLogo />
        <span class="text-xl font-bold">iTask</span>
      </div>
      <h1 class="text-[28px] font-bold tracking-[-0.02em]">Choose a new password</h1>
      <div v-if="done" role="status" class="flex flex-col gap-4">
        <p class="text-[15px]">✅ Your password was changed.</p>
        <RouterLink
          to="/sign-in"
          class="press flex h-12 items-center justify-center rounded-full bg-accent text-[15px] font-semibold text-white no-underline"
          >Sign in</RouterLink
        >
      </div>
      <form v-else class="flex flex-col gap-4" @submit.prevent="submit">
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
        <label v-if="!route.query.code" class="flex flex-col gap-1.5 text-[13px] font-semibold">
          Reset code
          <input
            v-model="code"
            type="text"
            autocomplete="one-time-code"
            required
            class="h-[46px] rounded-[var(--radius-control)] border border-line px-3.5 text-[15px] font-normal"
          />
        </label>
        <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
          New password
          <input
            v-model="password"
            type="password"
            autocomplete="new-password"
            aria-describedby="reset-password-hint"
            required
            class="h-[46px] rounded-[var(--radius-control)] border border-line px-3.5 text-[15px] font-normal"
          />
        </label>
        <span id="reset-password-hint" class="-mt-2 text-xs text-muted">{{ strength.hint }}</span>
        <p v-if="error" role="alert" class="text-sm text-red-600">{{ error }}</p>
        <button
          type="submit"
          :disabled="busy || strength.score < 4"
          class="press flex h-12 items-center justify-center gap-2 rounded-full bg-accent text-[15px] font-semibold text-white disabled:opacity-60"
        >
          <span v-if="busy" class="spinner" aria-hidden="true" />
          {{ busy ? 'Saving…' : 'Set new password' }}
        </button>
      </form>
    </div>
  </div>
</template>
