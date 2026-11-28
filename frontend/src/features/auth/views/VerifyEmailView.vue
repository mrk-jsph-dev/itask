<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import TurnstileWidget from '@/shared/ui/TurnstileWidget.vue'
import { turnstileEnabled } from '@/shared/ui/turnstile'
import { ApiError } from '@/shared/api/http'
import { authApi } from '../api'
import { useAuthStore } from '../store'
import { cleanCode, maskEmail, resendCooldownSeconds } from '../verify'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

/** From register (code just sent) or from sign-in ("Enter your code"), where none may have been sent yet. */
const fromQuery = typeof route.query.email === 'string' ? route.query.email : ''
const justSent = route.query.sent !== '0' && fromQuery !== ''
const email = ref(fromQuery)
const code = ref('')
const token = ref<string | null>(null)
const error = ref<string | null>(null)
const note = ref<string | null>(null)
const busy = ref(false)
const resending = ref(false)
const wait = ref(justSent ? resendCooldownSeconds : 0)
const widget = useTemplateRef('widget')
let timer: ReturnType<typeof setInterval> | undefined

const canVerify = computed(
  () => !busy.value && code.value.length === 6 && email.value.includes('@'),
)
const canResend = computed(
  () =>
    !resending.value &&
    wait.value === 0 &&
    email.value.includes('@') &&
    (!turnstileEnabled || token.value !== null),
)

function tick() {
  if (wait.value > 0) wait.value -= 1
}

onMounted(() => (timer = setInterval(tick, 1000)))
onBeforeUnmount(() => clearInterval(timer))

function onInput(event: Event) {
  code.value = cleanCode((event.target as HTMLInputElement).value)
  error.value = null
}

async function verify() {
  busy.value = true
  error.value = null
  try {
    await auth.verifyEmail(email.value.trim(), code.value)
    await router.replace('/')
  } catch (e) {
    error.value =
      e instanceof ApiError && (e.status === 400 || e.status === 403 || e.status === 429)
        ? e.message
        : 'Something went wrong.'
  } finally {
    busy.value = false
  }
}

async function resend() {
  resending.value = true
  error.value = null
  try {
    await authApi.resendCode(email.value.trim(), token.value)
    note.value = 'If this email is waiting to be confirmed, a new code is on its way.'
    wait.value = resendCooldownSeconds
  } catch (e) {
    error.value = e instanceof ApiError && e.status !== 500 ? e.message : 'Something went wrong.'
  } finally {
    resending.value = false
    widget.value?.reset()
  }
}

const inputClass =
  'h-[46px] rounded-[var(--radius-control)] border border-line px-3.5 text-[15px] font-normal'
</script>

<template>
  <div class="flex min-h-screen items-center justify-center px-4 py-12">
    <div class="fade-up flex w-full max-w-[460px] flex-col gap-6 rounded-[28px] bg-panel p-10">
      <div class="flex items-center gap-2.5">
        <AppLogo />
        <span class="text-xl font-bold">iTask</span>
      </div>
      <div class="flex flex-col gap-2">
        <h1 class="text-[28px] font-bold tracking-tight">Check your email</h1>
        <p class="text-[15px] text-muted">
          <template v-if="justSent">
            We sent a 6-digit code to
            <span class="font-semibold text-ink">{{ maskEmail(fromQuery) }}</span>. It expires in 10
            minutes.
          </template>
          <template v-else>Enter the 6-digit code from your sign-up email.</template>
        </p>
      </div>
      <form class="flex flex-col gap-4" @submit.prevent="verify">
        <label v-if="!fromQuery" class="flex flex-col gap-1.5 text-[13px] font-semibold">
          Email
          <input
            v-model="email"
            type="email"
            autocomplete="email"
            placeholder="you@example.com"
            required
            :class="inputClass"
          />
        </label>
        <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
          Code
          <input
            :value="code"
            type="text"
            inputmode="numeric"
            autocomplete="one-time-code"
            placeholder="123456"
            maxlength="12"
            required
            class="h-[56px] rounded-[var(--radius-control)] border border-line px-3.5 text-center text-[26px] font-semibold tracking-[0.4em]"
            @input="onInput"
          />
        </label>
        <p v-if="error" role="alert" class="text-sm text-danger">{{ error }}</p>
        <p v-else-if="note" role="status" class="text-sm text-muted">{{ note }}</p>
        <button
          type="submit"
          :disabled="!canVerify"
          class="press flex h-12 items-center justify-center gap-2 rounded-full bg-accent text-[15px] font-semibold text-on-accent disabled:opacity-60"
        >
          <span v-if="busy" class="spinner" aria-hidden="true" />
          {{ busy ? 'Checking…' : 'Verify and continue' }}
        </button>
      </form>
      <div class="flex flex-col gap-3 border-t border-line-soft pt-4">
        <TurnstileWidget ref="widget" v-model:token="token" />
        <button
          type="button"
          class="ghost h-10 rounded-full border border-line px-4 text-sm font-semibold disabled:opacity-60"
          :disabled="!canResend"
          @click="resend"
        >
          {{ wait > 0 ? `Send a new code in ${wait}s` : 'Send a new code' }}
        </button>
        <p class="text-center text-sm text-muted">
          Wrong email?
          <RouterLink to="/register" class="font-semibold text-accent-strong no-underline"
            >Sign up again</RouterLink
          >
          ·
          <RouterLink to="/sign-in" class="font-semibold text-accent-strong no-underline"
            >Sign in</RouterLink
          >
        </p>
      </div>
    </div>
  </div>
</template>
