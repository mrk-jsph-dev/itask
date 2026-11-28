<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'
import { RouterLink } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import TurnstileWidget from '@/shared/ui/TurnstileWidget.vue'
import { turnstileEnabled } from '@/shared/ui/turnstile'
import { authApi } from '../api'

const email = ref('')
const token = ref<string | null>(null)
const busy = ref(false)
const sent = ref(false)
const error = ref<string | null>(null)
const widget = useTemplateRef('widget')

const canSubmit = computed(() => !busy.value && (!turnstileEnabled || token.value !== null))

async function submit() {
  busy.value = true
  error.value = null
  try {
    await authApi.forgotPassword(email.value.trim(), token.value)
    sent.value = true
  } catch {
    error.value = "Couldn't send the email. Try again in a minute."
    widget.value?.reset()
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center p-4">
    <div
      class="fade-up flex w-full max-w-[440px] flex-col gap-6 rounded-[28px] bg-panel p-8 shadow-card"
    >
      <div class="flex items-center gap-2.5">
        <AppLogo />
        <span class="text-xl font-bold">iTask</span>
      </div>
      <div class="flex flex-col gap-2">
        <h1 class="text-[28px] font-bold tracking-[-0.02em]">Forgot password</h1>
        <p class="text-[15px] text-muted">
          Enter your email and we'll send you a link to choose a new password.
        </p>
      </div>
      <p v-if="sent" role="status" class="rounded-[14px] bg-hover p-4 text-sm leading-[1.5]">
        📬 If an account uses <strong>{{ email }}</strong
        >, a reset link is on its way. It works once.
      </p>
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
        <TurnstileWidget ref="widget" v-model:token="token" />
        <p v-if="error" role="alert" class="text-sm text-danger">{{ error }}</p>
        <button
          type="submit"
          :disabled="!canSubmit"
          class="press flex h-12 items-center justify-center gap-2 rounded-full bg-accent text-[15px] font-semibold text-on-accent disabled:opacity-60"
        >
          <span v-if="busy" class="spinner" aria-hidden="true" />
          {{ busy ? 'Sending…' : 'Send reset link' }}
        </button>
      </form>
      <RouterLink
        to="/sign-in"
        class="text-center text-sm font-semibold text-accent-strong no-underline"
        >Back to sign in</RouterLink
      >
    </div>
  </div>
</template>
