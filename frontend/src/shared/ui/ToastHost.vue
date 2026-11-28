<script setup lang="ts">
import { dismissToast, runToastAction, toasts } from './toast'
</script>

<template>
  <div
    class="pointer-events-none fixed inset-x-0 bottom-4 z-50 flex flex-col items-center gap-2 px-4"
    role="status"
    aria-live="polite"
  >
    <div
      v-for="item in toasts"
      :key="item.id"
      class="toast-in pointer-events-auto flex max-w-md items-center gap-3 rounded-full px-5 py-3 text-sm font-medium text-on-toast shadow-pop"
      :class="item.tone === 'error' ? 'bg-danger-fill' : 'bg-toast'"
    >
      <span>{{ item.text }}</span>
      <button
        v-if="item.action"
        type="button"
        class="font-semibold text-toast-action hover:text-on-toast"
        @click="runToastAction(item.id)"
      >
        {{ item.action.label }}
      </button>
      <button
        type="button"
        class="text-on-toast/70 hover:text-on-toast"
        aria-label="Dismiss"
        @click="dismissToast(item.id)"
      >
        ✕
      </button>
    </div>
  </div>
</template>
