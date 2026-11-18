<script setup lang="ts">
withDefaults(defineProps<{ height?: string; width?: string }>(), { height: '16px', width: '100%' })
</script>

<template>
  <div class="skel" :style="{ height, width }" aria-hidden="true" />
</template>
