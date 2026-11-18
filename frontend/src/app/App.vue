<script setup lang="ts">
import { RouterView } from 'vue-router'

import ToastHost from '@/shared/ui/ToastHost.vue'
</script>

<template>
  <RouterView />
  <ToastHost />
</template>
