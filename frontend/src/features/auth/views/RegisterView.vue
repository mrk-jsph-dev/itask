<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import TurnstileWidget from '@/shared/ui/TurnstileWidget.vue'
import { turnstileEnabled } from '@/shared/ui/turnstile'
import { ApiError } from '@/shared/api/http'
import ExternalButtons from '../components/ExternalButtons.vue'
import { passwordStrength } from '../passwordStrength'
import { useAuthStore } from '../store'

const auth = useAuthStore()
const router = useRouter()

const displayName = ref('')
const email = ref('')
const password = ref('')
const token = ref<string | null>(null)
const errors = ref<string[]>([])
const busy = ref(false)
const widget = useTemplateRef('widget')

const strength = computed(() => passwordStrength(password.value))
const canSubmit = computed(
  () => !busy.value && strength.value.score === 4 && (!turnstileEnabled || token.value !== null),
)

const inputClass =
  'h-[46px] rounded-[var(--radius-control)] border border-line px-3.5 text-[15px] font-normal'

async function submit() {
  busy.value = true
  errors.value = []
  try {
    const sentTo = await auth.register(
      {
        displayName: displayName.value.trim(),
        email: email.value.trim(),
        password: password.value,
      },
      token.value,
    )
    await router.replace({ name: 'verify-email', query: { email: sentTo } })
  } catch (e) {
    errors.value =
      e instanceof ApiError && e.errors
        ? Object.values(e.errors).flat()
        : [
            e instanceof ApiError && (e.status === 400 || e.status === 429)
              ? e.message
              : 'Something went wrong.',
          ]
    widget.value?.reset()
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center px-4 py-12">
    <div class="fade-up flex w-full max-w-[460px] flex-col gap-6 rounded-[28px] bg-panel p-10">
      <div class="flex items-center gap-2.5">
        <AppLogo />
        <span class="text-xl font-bold">iTask</span>
      </div>
      <div class="flex flex-col gap-2">
        <h1 class="text-[28px] font-bold tracking-tight">Create your account</h1>
        <p class="text-[15px] text-muted">Free forever for personal boards.</p>
      </div>
      <form class="flex flex-col gap-4" @submit.prevent="submit">
        <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
          Full name
          <input
            v-model="displayName"
            type="text"
            autocomplete="name"
            placeholder="Your name"
            maxlength="100"
            required
            :class="inputClass"
          />
        </label>
        <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
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
        <div class="flex flex-col gap-1.5">
          <label for="register-password" class="text-[13px] font-semibold">Password</label>
          <input
            id="register-password"
            v-model="password"
            type="password"
            autocomplete="new-password"
            placeholder="At least 10 characters"
            aria-describedby="password-hint"
            required
            :class="inputClass"
          />
          <div
            class="mt-1 grid grid-cols-4 gap-1.5"
            role="meter"
            aria-label="Password strength"
            aria-valuemin="0"
            aria-valuemax="4"
            :aria-valuenow="strength.score"
            :aria-valuetext="strength.label || 'Empty'"
          >
            <span
              v-for="n in 4"
              :key="n"
              data-testid="strength-bar"
              class="h-1 rounded transition-colors duration-200"
              :class="n <= strength.score ? 'bg-accent' : 'bg-line-soft'"
            />
          </div>
          <span id="password-hint" class="text-xs text-muted">
            {{ [strength.label, strength.hint].filter(Boolean).join('. ') }}
          </span>
        </div>
        <TurnstileWidget ref="widget" v-model:token="token" />
        <ul v-if="errors.length" role="alert" class="flex flex-col gap-1 text-sm text-red-600">
          <li v-for="message in errors" :key="message">{{ message }}</li>
        </ul>
        <button
          type="submit"
          :disabled="!canSubmit"
          class="press mt-1 flex h-12 items-center justify-center gap-2 rounded-full bg-accent text-[15px] font-semibold text-white disabled:opacity-60"
        >
          <span v-if="busy" class="spinner" aria-hidden="true" />
          {{ busy ? 'Creating account…' : 'Create account' }}
        </button>
      </form>
      <ExternalButtons :disabled="busy" />
      <p class="text-center text-sm text-muted">
        Already have an account?
        <RouterLink to="/sign-in" class="font-semibold text-accent-strong no-underline"
          >Sign in</RouterLink
        >
      </p>
    </div>
  </div>
</template>
