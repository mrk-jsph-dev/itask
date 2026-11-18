<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import { useAuthStore } from '@/features/auth/store'

const auth = useAuthStore()
</script>

<template>
  <div class="min-h-screen p-4 md:p-6">
    <header class="mx-auto flex max-w-7xl items-center justify-between gap-4 pb-4">
      <RouterLink to="/" class="flex items-center gap-2 text-lg font-bold text-ink no-underline">
        <AppLogo :size="30" />
        iTask
      </RouterLink>
      <nav class="flex items-center gap-1 rounded-full bg-panel p-1 text-sm font-medium">
        <RouterLink
          to="/"
          class="ghost rounded-full px-4 py-2 text-muted"
          active-class="!bg-ink !text-white"
          >🏠 Home</RouterLink
        >
      </nav>
      <span class="text-sm text-muted">{{ auth.user?.displayName || auth.user?.email }}</span>
    </header>
    <main class="mx-auto max-w-7xl rounded-[var(--radius-panel)] bg-panel p-6 md:p-8">
      <RouterView />
    </main>
  </div>
</template>
