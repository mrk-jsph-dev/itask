<script setup lang="ts">
import { computed } from 'vue'

import { initials } from './initials'

const props = withDefaults(
  defineProps<{ name?: string | null; email?: string; color?: string; size?: number }>(),
  { name: null, email: '', color: '#1D5BF5', size: 36 },
)

const text = computed(() => initials(props.name, props.email))
</script>

<template>
  <span
    class="inline-flex shrink-0 items-center justify-center rounded-full font-semibold text-white select-none"
    :style="{
      width: `${size}px`,
      height: `${size}px`,
      backgroundColor: color,
      fontSize: `${Math.round(size * 0.38)}px`,
    }"
    aria-hidden="true"
    >{{ text }}</span
  >
</template>
