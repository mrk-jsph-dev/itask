<script setup lang="ts">
import { online } from './network'
</script>

<template>
  <div
    v-if="!online"
    role="status"
    class="fade-up mx-5 mt-4 rounded-2xl bg-amber-50 px-4 py-2.5 text-sm text-amber-900 md:mx-7"
    data-testid="offline-banner"
  >
    📴 You're offline. Showing saved boards, read-only.
  </div>
</template>
