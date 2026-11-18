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
      class="fade-up pointer-events-auto flex max-w-md items-center gap-3 rounded-full px-5 py-3 text-sm font-medium text-white shadow-[0_10px_24px_rgb(17_24_39/0.18)]"
      :class="item.tone === 'error' ? 'bg-red-700' : 'bg-ink'"
    >
      <span>{{ item.text }}</span>
      <button
        v-if="item.action"
        type="button"
        class="font-semibold text-sky-300 hover:text-white"
        @click="runToastAction(item.id)"
      >
        {{ item.action.label }}
      </button>
      <button
        type="button"
        class="text-white/70 hover:text-white"
        aria-label="Dismiss"
        @click="dismissToast(item.id)"
      >
        ✕
      </button>
    </div>
  </div>
</template>
