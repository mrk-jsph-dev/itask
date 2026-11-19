<script setup lang="ts">
defineProps<{ cardTouched: boolean; busy: boolean }>()
const emit = defineEmits<{ stop: []; cancel: [] }>()
</script>

<template>
  <div
    role="status"
    class="pop fixed bottom-6 left-1/2 z-40 flex w-[min(460px,calc(100%-32px))] -translate-x-1/2 items-center gap-3 rounded-full border border-line-soft bg-panel py-2 pr-2 pl-4 text-sm shadow-[0_18px_40px_rgb(17_24_39/0.18)]"
  >
    <span class="h-2.5 w-2.5 shrink-0 animate-pulse rounded-full bg-red-600" aria-hidden="true" />
    <span class="min-w-0 flex-1">
      {{
        cardTouched
          ? 'Recording. Do the rest of the steps, then press Stop.'
          : 'Recording. Move a card into a list (or complete it), then do the steps you repeat.'
      }}
    </span>
    <button
      type="button"
      class="press h-8 rounded-full bg-accent px-3 font-semibold text-white disabled:opacity-60"
      :disabled="busy"
      @click="emit('stop')"
    >
      ⏹ Stop
    </button>
    <button type="button" class="ghost h-8 rounded-full px-3 font-semibold" @click="emit('cancel')">
      Cancel
    </button>
  </div>
</template>
