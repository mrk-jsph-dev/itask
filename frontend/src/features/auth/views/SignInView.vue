<script setup lang="ts">
import { computed, onMounted, ref, useTemplateRef } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import TurnstileWidget from '@/shared/ui/TurnstileWidget.vue'
import { turnstileEnabled } from '@/shared/ui/turnstile'
import { ApiError } from '@/shared/api/http'
import { authApi, type DemoRole } from '../api'
import { useAuthStore } from '../store'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const token = ref<string | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)
const demoEnabled = ref(false)
const widget = useTemplateRef('widget')

const demoRoles: { role: DemoRole; label: string }[] = [
  { role: 'admin', label: 'Admin' },
  { role: 'owner', label: 'Owner' },
  { role: 'editor', label: 'Editor' },
  { role: 'viewer', label: 'Viewer' },
]

const canSubmit = computed(() => !busy.value && (!turnstileEnabled || token.value !== null))

onMounted(async () => {
  try {
    demoEnabled.value = (await authApi.demoStatus()).enabled
  } catch {
    demoEnabled.value = false
  }
})

async function goNext() {
  const next =
    typeof route.query.next === 'string' &&
    route.query.next.startsWith('/') &&
    !route.query.next.startsWith('//')
      ? route.query.next
      : '/'
  await router.replace(next)
}

async function submit() {
  busy.value = true
  error.value = null
  try {
    await auth.signIn(email.value, password.value, token.value)
    await goNext()
  } catch (e) {
    error.value =
      e instanceof ApiError && e.status === 401
        ? 'Email or password is incorrect, or the account is locked for a few minutes.'
        : e instanceof ApiError && e.status === 400
          ? e.message
          : 'Something went wrong.'
    widget.value?.reset()
  } finally {
    busy.value = false
  }
}

async function tryDemo(role: DemoRole) {
  busy.value = true
  error.value = null
  try {
    await auth.demoSignIn(role)
    await goNext()
  } catch {
    error.value = 'The demo is not available right now.'
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
        <TurnstileWidget ref="widget" v-model:token="token" />
        <p v-if="error" role="alert" class="text-sm text-red-600">{{ error }}</p>
        <button
          type="submit"
          :disabled="!canSubmit"
          class="press mt-1 h-12 rounded-full bg-accent text-[15px] font-semibold text-white disabled:opacity-60"
        >
          {{ busy ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>
      <section v-if="demoEnabled" class="fade-in flex flex-col gap-3" aria-labelledby="demo-title">
        <div class="flex items-center gap-3 text-xs font-semibold text-muted uppercase">
          <span class="h-px flex-1 bg-line-soft" />
          <h2 id="demo-title">Try a demo</h2>
          <span class="h-px flex-1 bg-line-soft" />
        </div>
        <div class="grid grid-cols-2 gap-2 sm:grid-cols-4">
          <button
            v-for="(demo, i) in demoRoles"
            :key="demo.role"
            type="button"
            :disabled="busy"
            :style="{ '--i': i }"
            class="fade-up ghost h-10 rounded-full border border-line text-sm font-semibold disabled:opacity-60"
            :aria-label="`Sign in as demo ${demo.label}`"
            @click="tryDemo(demo.role)"
          >
            {{ demo.label }}
          </button>
        </div>
      </section>
      <p class="text-center text-sm text-muted">
        New to iTask?
        <RouterLink to="/register" class="font-semibold text-accent-strong no-underline"
          >Create an account</RouterLink
        >
      </p>
    </div>
  </div>
</template>
