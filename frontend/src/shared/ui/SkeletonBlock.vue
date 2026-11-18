<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

/** Stays invisible for `delay` ms so loads that finish quickly never flash a skeleton. */
const props = withDefaults(defineProps<{ height?: string; width?: string; delay?: number }>(), {
  height: '16px',
  width: '100%',
  delay: 150,
})

const shown = ref(props.delay <= 0)
let timer: ReturnType<typeof setTimeout> | undefined

onMounted(() => {
  if (!shown.value) timer = setTimeout(() => (shown.value = true), props.delay)
})
onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <div
    :class="shown ? 'skel fade-in' : ''"
    :style="{ height, width }"
    data-testid="skeleton"
    aria-hidden="true"
  />
</template>
