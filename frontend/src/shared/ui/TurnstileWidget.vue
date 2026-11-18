<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * Cloudflare Turnstile challenge. Renders nothing when VITE_TURNSTILE_SITE_KEY is empty;
 * the bound token then stays null and the API skips verification.
 */
interface TurnstileApi {
  render(el: HTMLElement, options: Record<string, unknown>): string
  reset(id: string): void
  remove(id: string): void
}

declare global {
  interface Window {
    turnstile?: TurnstileApi
  }
}

const token = defineModel<string | null>('token', { default: null })

const siteKey = import.meta.env.VITE_TURNSTILE_SITE_KEY
const el = ref<HTMLDivElement>()
let widgetId: string | undefined

const scriptUrl = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'
let loading: Promise<void> | undefined

function loadScript(): Promise<void> {
  loading ??= new Promise((resolve, reject) => {
    const script = document.createElement('script')
    script.src = scriptUrl
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('Turnstile failed to load'))
    document.head.appendChild(script)
  })
  return loading
}

onMounted(async () => {
  if (!siteKey || !el.value) return
  await loadScript()
  widgetId = window.turnstile?.render(el.value, {
    sitekey: siteKey,
    callback: (value: string) => (token.value = value),
    'expired-callback': () => (token.value = null),
    'error-callback': () => (token.value = null),
  })
})

onBeforeUnmount(() => {
  if (widgetId) window.turnstile?.remove(widgetId)
})

/** Tokens are single use: call after every submit attempt. */
function reset() {
  token.value = null
  if (widgetId) window.turnstile?.reset(widgetId)
}

defineExpose({ reset })
</script>

<template>
  <div v-if="siteKey" ref="el" class="min-h-[65px]" />
</template>
