<script setup lang="ts">
withDefaults(defineProps<{ size?: number }>(), { size: 34 })
</script>

<template>
  <svg :width="size" :height="size" viewBox="0 0 48 48" role="img" aria-label="iTask logo">
    <rect width="48" height="48" rx="13" fill="var(--color-accent)" />
    <circle cx="14.5" cy="13" r="3.6" fill="#FFFFFF" />
    <path
      d="M14.5 21.5L21.5 33L36 14.5"
      fill="none"
      stroke="#FFFFFF"
      stroke-width="5.2"
      stroke-linecap="round"
      stroke-linejoin="round"
    />
  </svg>
</template>
