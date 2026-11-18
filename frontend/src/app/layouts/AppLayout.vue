<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'
import { RouterLink, RouterView, useRouter } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { useAuthStore } from '@/features/auth/store'

const auth = useAuthStore()
const router = useRouter()

const menuOpen = ref(false)
const menu = useTemplateRef('menu')

function closeOnOutside(event: MouseEvent) {
  if (menuOpen.value && !menu.value?.contains(event.target as Node)) menuOpen.value = false
}

function closeOnEscape(event: KeyboardEvent) {
  if (event.key === 'Escape') menuOpen.value = false
}

onMounted(() => {
  document.addEventListener('click', closeOnOutside)
  document.addEventListener('keydown', closeOnEscape)
})
onBeforeUnmount(() => {
  document.removeEventListener('click', closeOnOutside)
  document.removeEventListener('keydown', closeOnEscape)
})

async function signOut() {
  menuOpen.value = false
  try {
    await auth.signOut()
  } catch {
    // The store has already cleared the local session.
  }
  await router.replace('/sign-in')
}
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
          exact-active-class="!bg-ink !text-white"
          >🏠 Home</RouterLink
        >
        <RouterLink
          to="/activity"
          class="ghost rounded-full px-4 py-2 text-muted"
          active-class="!bg-ink !text-white"
          >⚡ Activity</RouterLink
        >
        <RouterLink
          v-if="auth.user?.isPlatformAdmin"
          to="/admin/users"
          class="ghost rounded-full px-4 py-2 text-muted"
          active-class="!bg-ink !text-white"
          >🛡 Admin</RouterLink
        >
      </nav>
      <div ref="menu" class="relative">
        <button
          type="button"
          class="press flex items-center rounded-full"
          aria-haspopup="menu"
          :aria-expanded="menuOpen"
          aria-controls="account-menu"
          :aria-label="`Account menu for ${auth.user?.displayName || auth.user?.email}`"
          @click="menuOpen = !menuOpen"
        >
          <UserAvatar
            :name="auth.user?.displayName"
            :email="auth.user?.email"
            :color="auth.user?.avatarColor"
          />
        </button>
        <div
          v-if="menuOpen"
          id="account-menu"
          role="menu"
          class="pop absolute right-0 z-20 mt-2 flex w-56 flex-col gap-1 rounded-2xl bg-panel p-2 shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
        >
          <div class="flex flex-col px-3 py-2">
            <span class="truncate text-sm font-semibold">{{ auth.user?.displayName }}</span>
            <span class="truncate text-xs text-muted">{{ auth.user?.email }}</span>
          </div>
          <RouterLink
            to="/profile"
            role="menuitem"
            class="ghost rounded-xl px-3 py-2 text-sm text-ink no-underline"
            @click="menuOpen = false"
            >👤 Profile</RouterLink
          >
          <button
            type="button"
            role="menuitem"
            class="ghost rounded-xl px-3 py-2 text-left text-sm"
            @click="signOut"
          >
            ↩ Sign out
          </button>
        </div>
      </div>
    </header>
    <main class="mx-auto max-w-7xl rounded-[var(--radius-panel)] bg-panel p-6 md:p-8">
      <RouterView />
    </main>
  </div>
</template>
