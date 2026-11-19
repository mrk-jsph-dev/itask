<script setup lang="ts">
import { applyUpdate, dismissUpdate, needRefresh } from './register'
</script>

<template>
  <div
    v-if="needRefresh"
    class="pointer-events-none fixed inset-x-0 bottom-20 z-50 flex justify-center px-4"
    role="status"
    aria-live="polite"
  >
    <div
      class="toast-in pointer-events-auto flex max-w-md items-center gap-3 rounded-full bg-ink px-5 py-3 text-sm font-medium text-white shadow-[0_10px_24px_rgb(17_24_39/0.18)]"
    >
      <span>A new version of iTask is ready.</span>
      <button
        type="button"
        class="font-semibold text-sky-300 hover:text-white"
        @click="applyUpdate"
      >
        Reload
      </button>
      <button
        type="button"
        class="text-white/70 hover:text-white"
        aria-label="Later"
        @click="dismissUpdate"
      >
        ✕
      </button>
    </div>
  </div>
</template>
