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
      class="toast-in pointer-events-auto flex max-w-md items-center gap-3 rounded-full bg-toast px-5 py-3 text-sm font-medium text-panel shadow-pop"
    >
      <span>A new version of iTask is ready.</span>
      <button
        type="button"
        class="font-semibold text-toast-action hover:text-on-toast"
        @click="applyUpdate"
      >
        Reload
      </button>
      <button
        type="button"
        class="text-on-toast/70 hover:text-on-toast"
        aria-label="Later"
        @click="dismissUpdate"
      >
        ✕
      </button>
    </div>
  </div>
</template>
