<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import { OFFLINE_HINT, online } from '@/shared/pwa/network'
import AppLogo from '@/shared/ui/AppLogo.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { useAuthStore } from '@/features/auth/store'
import { useBoardsStore } from '@/features/boards/store'
import { badgeText } from '@/features/notifications/format'
import { useNotificationsStore } from '@/features/notifications/store'
import { boardRouteNames, sidebarBoards } from './sidebar'

/** `compact`: the icon rail; labels stay for screen readers and tooltips. */
const props = withDefaults(defineProps<{ compact?: boolean; searchShortcut?: string }>(), {
  compact: false,
  searchShortcut: 'Ctrl K',
})
const emit = defineEmits<{ search: []; navigate: [] }>()

const auth = useAuthStore()
const boards = useBoardsStore()
const notifications = useNotificationsStore()
const unread = computed(() => badgeText(notifications.unreadCount))
const route = useRoute()
const router = useRouter()

const activeBoardId = computed(() =>
  typeof route.params.boardId === 'string' ? route.params.boardId : null,
)
const shownBoards = computed(() => sidebarBoards(boards.navBoards ?? []))
const shownMembers = 6
const members = computed(() =>
  (boardRouteNames as readonly string[]).includes(String(route.name)) &&
  boards.current?.id === activeBoardId.value
    ? boards.members
    : [],
)

const icons = {
  home: 'M3 11l9-7 9 7v9a1 1 0 01-1 1h-5v-6H9v6H4a1 1 0 01-1-1z',
  search: 'M11 4a7 7 0 100 14 7 7 0 000-14zM20 20l-3.5-3.5',
  activity: 'M13 2L3 14h9l-1 8 10-12h-9z',
  bell: 'M6 8a6 6 0 1112 0c0 7 3 9 3 9H3s3-2 3-9M10 21h4',
  admin: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z',
  profile: 'M12 4a4 4 0 100 8 4 4 0 000-8zM4 21a8 8 0 0116 0',
  signOut: 'M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4M16 17l5-5-5-5M21 12H9',
}

const pill = computed(() => [
  'ghost flex h-11 items-center gap-3 rounded-full text-[15px] font-medium text-[#374151] no-underline',
  props.compact ? 'w-11 justify-center self-center' : 'px-3.5',
])
const active = '!bg-panel !font-semibold !text-ink shadow-[0_1px_2px_rgb(17_24_39/0.06)]'
const label = computed(() => (props.compact ? 'sr-only' : 'truncate'))

function newBoard() {
  boards.creating = true
  emit('navigate')
}

async function signOut() {
  emit('navigate')
  try {
    await auth.signOut()
  } catch {
    // The store has already cleared the local session.
  }
  await router.replace('/sign-in')
}
</script>

<template>
  <nav aria-label="Main" class="flex h-full flex-col gap-6 px-1 py-2">
    <RouterLink
      to="/"
      class="flex items-center gap-2.5 text-lg font-bold text-ink no-underline"
      :class="compact ? 'justify-center' : 'px-3'"
      @click="emit('navigate')"
    >
      <AppLogo :size="30" />
      <span :class="label">iTask</span>
    </RouterLink>

    <div class="flex flex-col gap-1">
      <RouterLink
        to="/"
        :class="pill"
        :exact-active-class="active"
        :title="compact ? 'Home' : undefined"
        @click="emit('navigate')"
      >
        <svg
          class="h-[18px] w-[18px] shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="icons.home" />
        </svg>
        <span :class="label">Home</span>
      </RouterLink>
      <button
        type="button"
        :class="pill"
        aria-haspopup="dialog"
        :aria-keyshortcuts="searchShortcut === '⌘K' ? 'Meta+K' : 'Control+K'"
        :title="compact ? `Search (${searchShortcut})` : undefined"
        @click="emit('search')"
      >
        <svg
          class="h-[18px] w-[18px] shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          aria-hidden="true"
        >
          <path :d="icons.search" />
        </svg>
        <span :class="label">Search</span>
        <kbd
          v-if="!compact"
          class="ml-auto font-sans text-xs font-normal text-muted"
          aria-hidden="true"
          >{{ searchShortcut }}</kbd
        >
      </button>
      <RouterLink
        to="/activity"
        :class="pill"
        :active-class="active"
        :title="compact ? 'Activity' : undefined"
        @click="emit('navigate')"
      >
        <svg
          class="h-[18px] w-[18px] shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="icons.activity" />
        </svg>
        <span :class="label">Activity</span>
      </RouterLink>
      <RouterLink
        to="/notifications"
        :class="[pill, 'relative']"
        :active-class="active"
        :title="compact ? 'Notifications' : undefined"
        @click="emit('navigate')"
      >
        <svg
          class="h-[18px] w-[18px] shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="icons.bell" />
        </svg>
        <span :class="label">Notifications</span>
        <span
          v-if="unread"
          class="flex h-5 min-w-5 items-center justify-center rounded-full bg-accent px-1.5 text-[11px] font-bold text-white"
          :class="compact ? 'absolute -top-0.5 -right-0.5 h-4 min-w-4 px-1 text-[10px]' : 'ml-auto'"
          ><span aria-hidden="true">{{ unread }}</span
          ><span class="sr-only">, {{ notifications.unreadCount }} unread</span></span
        >
      </RouterLink>
      <RouterLink
        v-if="auth.user?.isPlatformAdmin"
        to="/admin/users"
        :class="pill"
        :active-class="active"
        :title="compact ? 'Admin' : undefined"
        @click="emit('navigate')"
      >
        <svg
          class="h-[18px] w-[18px] shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="icons.admin" />
        </svg>
        <span :class="label">Admin</span>
      </RouterLink>
    </div>

    <section class="flex flex-col gap-1" aria-labelledby="sidebar-boards">
      <div
        class="flex items-center pb-1.5"
        :class="compact ? 'justify-center' : 'justify-between px-3.5'"
      >
        <h2
          id="sidebar-boards"
          class="text-[13px] font-medium text-muted"
          :class="{ 'sr-only': compact }"
        >
          Boards
        </h2>
        <button
          type="button"
          class="ghost flex h-7 w-7 items-center justify-center rounded-full text-lg text-muted disabled:cursor-not-allowed disabled:opacity-50"
          aria-label="New board"
          :title="online ? 'New board' : OFFLINE_HINT"
          :disabled="!online"
          @click="newBoard"
        >
          +
        </button>
      </div>
      <ul class="flex flex-col gap-1">
        <li v-for="board in shownBoards" :key="board.id" class="flex flex-col">
          <RouterLink
            :to="{ name: 'board', params: { boardId: board.id } }"
            :class="[
              pill,
              board.id === activeBoardId ? '!bg-[#F6F8FB] !font-semibold !text-ink' : '',
            ]"
            :aria-current="board.id === activeBoardId ? 'page' : undefined"
            :title="compact ? board.name : undefined"
            @click="emit('navigate')"
          >
            <span class="w-5 shrink-0 text-center text-base" aria-hidden="true">{{
              board.emoji ?? '📋'
            }}</span>
            <span :class="label">{{ board.name }}</span>
            <span v-if="board.isStarred && !compact" class="ml-auto text-xs text-amber-500"
              ><span aria-hidden="true">★</span><span class="sr-only">Starred</span></span
            >
          </RouterLink>
        </li>
      </ul>
      <p v-if="boards.navBoards?.length === 0 && !compact" class="px-3.5 text-[13px] text-muted">
        No boards yet.
      </p>
    </section>

    <section v-if="members.length" class="flex flex-col gap-2.5" aria-labelledby="sidebar-members">
      <h2
        id="sidebar-members"
        class="text-[13px] font-medium text-muted"
        :class="compact ? 'sr-only' : 'px-3.5'"
      >
        Board members
      </h2>
      <ul class="flex flex-col gap-2.5">
        <li
          v-for="member in members.slice(0, shownMembers)"
          :key="member.userId"
          class="flex items-center gap-3 text-[15px] text-[#374151]"
          :class="compact ? 'justify-center' : 'px-3.5'"
          :title="member.displayName || member.email"
        >
          <UserAvatar
            :name="member.displayName"
            :email="member.email"
            :color="member.avatarColor"
            :size="28"
          />
          <span :class="label">{{
            member.userId === auth.user?.id ? 'You' : member.displayName || member.email
          }}</span>
        </li>
        <li
          v-if="members.length > shownMembers"
          class="text-[13px] text-muted"
          :class="compact ? 'text-center' : 'px-3.5'"
        >
          +{{ members.length - shownMembers }} more
        </li>
      </ul>
    </section>

    <div class="mt-auto flex flex-col gap-1">
      <RouterLink
        to="/profile"
        :class="pill"
        :active-class="active"
        :title="compact ? 'Profile' : undefined"
        @click="emit('navigate')"
      >
        <svg
          class="h-[18px] w-[18px] shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="icons.profile" />
        </svg>
        <span :class="label">Profile</span>
      </RouterLink>
      <button
        type="button"
        :class="pill"
        :title="compact ? 'Sign out' : undefined"
        @click="signOut"
      >
        <svg
          class="h-[18px] w-[18px] shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="icons.signOut" />
        </svg>
        <span :class="label">Sign out</span>
      </button>
    </div>
  </nav>
</template>
