<script setup lang="ts">
import {
  markRaw,
  nextTick,
  onBeforeUnmount,
  onMounted,
  provide,
  ref,
  useTemplateRef,
  watch,
} from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'

import OfflineBanner from '@/shared/pwa/OfflineBanner.vue'
import AppLogo from '@/shared/ui/AppLogo.vue'
import { panelHeaderExtra } from '@/shared/ui/panelHeader'
import { useAuthStore } from '@/features/auth/store'
import NewBoardDialog from '@/features/boards/components/NewBoardDialog.vue'
import { useBoardsStore } from '@/features/boards/store'
import NotificationBell from '@/features/notifications/components/NotificationBell.vue'
import { useNotificationsStore } from '@/features/notifications/store'
import SearchPalette from '@/features/search/components/SearchPalette.vue'
import AppSidebar from './AppSidebar.vue'
import { useShellMode } from './sidebar'

const auth = useAuthStore()
const boards = useBoardsStore()
const notifications = useNotificationsStore()
const route = useRoute()
const mode = useShellMode()

const searchOpen = ref(false)
/** The sidebar sheet on phones. */
const drawerOpen = ref(false)
const drawer = useTemplateRef('drawer')
const menuButton = useTemplateRef('menuButton')
const searchShortcut = /Mac|iPhone|iPad/.test(navigator.userAgent) ? '⌘K' : 'Ctrl K'

provide(panelHeaderExtra, markRaw(NotificationBell))

function openSearch() {
  drawerOpen.value = false
  searchOpen.value = true
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && drawerOpen.value) closeDrawer()
  if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey) && !event.altKey) {
    event.preventDefault()
    drawerOpen.value = false
    searchOpen.value = !searchOpen.value
  }
}

async function openDrawer() {
  drawerOpen.value = true
  await nextTick()
  drawer.value?.querySelector<HTMLElement>('a, button')?.focus()
}

function closeDrawer() {
  drawerOpen.value = false
  menuButton.value?.focus()
}

watch(mode, (value) => {
  if (value !== 'sheet') drawerOpen.value = false
})
watch(
  () => route.fullPath,
  () => (drawerOpen.value = false),
)

onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  if (boards.navBoards === null) boards.loadNav().catch(() => (boards.navBoards = []))
  notifications.startPolling()
})
onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown)
  notifications.stopPolling()
})
</script>

<template>
  <div class="flex min-h-screen flex-col gap-2 p-2 md:flex-row md:p-4">
    <header v-if="mode === 'sheet'" class="flex items-center gap-2 px-1 py-1">
      <button
        ref="menuButton"
        type="button"
        class="ghost flex h-11 w-11 items-center justify-center rounded-full"
        aria-label="Open menu"
        :aria-expanded="drawerOpen"
        aria-controls="app-drawer"
        @click="openDrawer"
      >
        <svg
          width="20"
          height="20"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          aria-hidden="true"
        >
          <path d="M4 7h16M4 12h16M4 17h16" />
        </svg>
      </button>
      <RouterLink to="/" class="flex items-center gap-2 text-lg font-bold text-ink no-underline">
        <AppLogo :size="28" />
        iTask
      </RouterLink>
    </header>
    <aside
      v-else
      class="sticky top-4 shrink-0 self-start overflow-y-auto"
      :class="mode === 'rail' ? 'w-[60px]' : 'w-[232px]'"
      style="height: calc(100vh - 32px)"
    >
      <AppSidebar
        :compact="mode === 'rail'"
        :search-shortcut="searchShortcut"
        @search="openSearch"
      />
    </aside>

    <Teleport to="body">
      <template v-if="drawerOpen">
        <div class="fade-in fixed inset-0 z-40 bg-ink/30" aria-hidden="true" @click="closeDrawer" />
        <div
          id="app-drawer"
          ref="drawer"
          role="dialog"
          aria-modal="true"
          aria-label="Menu"
          class="drawer fixed inset-y-0 left-0 z-40 w-[280px] max-w-[85vw] overflow-y-auto bg-shell p-3 shadow-[0_10px_40px_rgb(17_24_39/0.2)]"
        >
          <AppSidebar
            :search-shortcut="searchShortcut"
            @search="openSearch"
            @navigate="drawerOpen = false"
          />
        </div>
      </template>
    </Teleport>

    <SearchPalette
      v-if="searchOpen && auth.user"
      :user-id="auth.user.id"
      @close="searchOpen = false"
    />
    <main
      class="flex min-w-0 flex-1 flex-col rounded-[var(--radius-panel)] bg-panel shadow-[0_1px_2px_rgb(17_24_39/0.04)]"
    >
      <OfflineBanner />
      <RouterView />
    </main>
    <NewBoardDialog v-if="boards.creating" @close="boards.creating = false" />
  </div>
</template>

<style scoped>
.drawer {
  animation: drawer-in var(--d-slow) var(--ease-out) both;
}
@keyframes drawer-in {
  from {
    opacity: 0;
    transform: translateX(-40px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
</style>
