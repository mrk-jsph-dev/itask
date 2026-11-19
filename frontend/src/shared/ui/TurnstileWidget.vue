<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

import { loadTurnstile } from './turnstile'

/**
 * Cloudflare Turnstile challenge. Renders nothing when VITE_TURNSTILE_SITE_KEY is empty;
 * the bound token then stays null and the API skips verification.
 */
const token = defineModel<string | null>('token', { default: null })

const siteKey = import.meta.env.VITE_TURNSTILE_SITE_KEY
const el = ref<HTMLDivElement>()
let widgetId: string | undefined

onMounted(async () => {
  if (!siteKey || !el.value) return
  await loadTurnstile()
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
