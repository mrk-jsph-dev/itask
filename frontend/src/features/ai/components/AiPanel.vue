<script setup lang="ts">
import { useAiStore } from '../store'

defineProps<{ title: string }>()
defineSlots<{ default(): unknown; actions?(): unknown }>()

const ai = useAiStore()
</script>

<template>
  <section
    class="fade-up flex w-full flex-col gap-3 rounded-[16px] border border-[#DCE6FE] bg-[#F5F8FF] p-4"
    :aria-label="title"
  >
    <h3 class="flex items-center gap-1.5 text-sm font-semibold text-accent-strong">
      <span aria-hidden="true">✨</span> {{ title }}
    </h3>
    <slot />
    <div class="flex flex-wrap items-center gap-2">
      <slot name="actions" />
      <p class="ml-auto text-xs text-muted">
        {{ ai.remaining }} of {{ ai.limit }} AI requests left today
      </p>
    </div>
  </section>
</template>
