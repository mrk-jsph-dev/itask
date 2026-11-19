<script setup lang="ts">
import { ref } from 'vue'
import { RouterView, useRouter } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import RouteProgress from '@/shared/ui/RouteProgress.vue'
import ToastHost from '@/shared/ui/ToastHost.vue'

/** The first navigation waits for the session check; the logo pulses meanwhile. */
const ready = ref(false)
void useRouter()
  .isReady()
  .finally(() => (ready.value = true))
</script>

<template>
  <div
    v-if="!ready"
    class="fade-in flex min-h-screen items-center justify-center"
    style="animation-delay: 150ms"
    role="status"
    aria-label="Loading iTask"
  >
    <AppLogo :size="56" class="pulse" />
  </div>
  <RouteProgress />
  <RouterView />
  <ToastHost />
</template>
