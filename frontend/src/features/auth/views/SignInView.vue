<script setup lang="ts">
import { computed, onMounted, ref, useTemplateRef } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import TurnstileWidget from '@/shared/ui/TurnstileWidget.vue'
import { turnstileEnabled } from '@/shared/ui/turnstile'
import { authApi, type DemoRole } from '../api'
import { signInErrorMessage } from '../signInError'
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

/** The right-hand preview: three mini columns, one card each. */
const preview = [
  {
    emoji: '📝',
    name: 'To do',
    count: 3,
    card: 'Wireframe profile page',
    priority: 'Medium',
    color: '#1D4ED8',
  },
  { emoji: '🚧', name: 'Doing', count: 4, card: 'Boards API', priority: 'High', color: '#B91C1C' },
  {
    emoji: '✅',
    name: 'Done',
    count: 2,
    card: 'Project scaffold',
    priority: 'Low',
    color: '#4B5563',
  },
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
    error.value = signInErrorMessage(e)
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
  <div class="flex min-h-screen p-2 md:p-4">
    <div
      class="flex flex-1 flex-wrap overflow-hidden rounded-[28px] bg-panel shadow-[0_1px_2px_rgb(17_24_39/0.04)]"
    >
      <div class="flex min-w-0 flex-[1_1_460px] items-center justify-center px-6 py-12">
        <div class="fade-up flex w-full max-w-[380px] flex-col gap-7">
          <div class="flex items-center gap-2.5">
            <AppLogo />
            <span class="text-xl font-bold">iTask</span>
          </div>
          <div class="flex flex-col gap-2">
            <h1 class="text-[30px] font-bold tracking-[-0.02em]">Sign in</h1>
            <p class="text-[15px] text-muted">Welcome back. Your tasks are where you left them.</p>
          </div>
          <form class="flex flex-col gap-4" @submit.prevent="submit">
            <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
              Email
              <input
                v-model="email"
                type="email"
                autocomplete="email"
                placeholder="you@example.com"
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
              class="press mt-1 flex h-12 items-center justify-center gap-2 rounded-full bg-accent text-[15px] font-semibold text-white disabled:opacity-60"
            >
              <span v-if="busy" class="spinner" aria-hidden="true" />
              {{ busy ? 'Signing in…' : 'Sign in' }}
            </button>
          </form>
          <section
            v-if="demoEnabled"
            class="fade-in flex flex-col gap-3"
            aria-labelledby="demo-title"
          >
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
      <div
        class="hidden min-w-0 flex-[1_1_560px] flex-col justify-center gap-8 bg-hover px-12 py-14 md:flex"
        aria-hidden="true"
      >
        <div class="flex max-w-[480px] flex-col gap-3">
          <p class="text-[34px] leading-[1.15] font-bold tracking-[-0.02em]">
            See every task, in every state, at a glance.
          </p>
          <p class="text-base leading-[1.55] text-muted">
            Boards, lists and calendars for you and your team.
          </p>
        </div>
        <div class="grid grid-cols-3 gap-3">
          <div
            v-for="(column, i) in preview"
            :key="column.name"
            class="fade-up flex flex-col gap-2 rounded-2xl border-[1.5px] border-dashed border-line p-2.5"
            :style="{ '--i': i + 2 }"
          >
            <span class="text-[13px] font-semibold"
              >{{ column.emoji }} {{ column.name }}
              <span class="text-muted">{{ column.count }}</span></span
            >
            <div class="flex flex-col gap-2 rounded-xl border border-line-soft bg-panel p-2.5">
              <span class="text-[13px] font-semibold">{{ column.card }}</span>
              <span class="text-xs font-semibold" :style="{ color: column.color }">{{
                column.priority
              }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
