<script setup lang="ts">
import { computed } from 'vue'

import { OFFLINE_HINT, online } from '@/shared/pwa/network'
import { useAiStore } from '../store'

const props = defineProps<{ label: string; action: string; compact?: boolean }>()
defineEmits<{ click: [] }>()

const ai = useAiStore()
const running = computed(() => ai.busy === props.action)
const disabled = computed(() => !online.value || ai.remaining === 0 || ai.busy !== null)
const title = computed(() => {
  if (!online.value) return OFFLINE_HINT
  if (ai.remaining === 0) return 'No AI requests left today'
  return `${props.label} (${ai.remaining} of ${ai.limit} AI requests left today)`
})
</script>

<template>
  <button
    type="button"
    class="press flex items-center gap-1.5 rounded-full border border-[#DCE6FE] bg-[#F5F8FF] font-semibold text-accent-strong disabled:cursor-not-allowed disabled:opacity-60"
    :class="compact ? 'h-9 px-3 text-sm' : 'h-[42px] px-3.5 text-sm'"
    :disabled="disabled"
    :title="title"
    :aria-busy="running"
    @click="$emit('click')"
  >
    <span v-if="running" class="spinner" aria-hidden="true" />
    <span v-else aria-hidden="true">✨</span>
    {{ label }}
  </button>
</template>
