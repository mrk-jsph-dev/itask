<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

import { ApiError } from '@/shared/api/http'
import { dayLabel, timeLabel } from '@/shared/ui/dates'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { toast } from '@/shared/ui/toast'
import type { AppNotification } from '../api'
import { badgeText } from '../format'
import { useNotificationsStore } from '../store'
import NotificationRow from './NotificationRow.vue'

const store = useNotificationsStore()
const router = useRouter()
const open = ref(false)
const accepting = ref<string | null>(null)
const panel = useTemplateRef('panel')
const trigger = useTemplateRef('trigger')

const badge = computed(() => badgeText(store.unreadCount))
/** Bumped when the count rises so the badge replays `.pop`. */
const popKey = ref(0)
watch(
  () => store.unreadCount,
  (count, before) => {
    if (count > before) popKey.value++
  },
)

const label = computed(() =>
  store.unreadCount ? `Notifications, ${store.unreadCount} unread` : 'Notifications',
)

async function toggle() {
  open.value = !open.value
  if (!open.value) return
  await nextTick()
  panel.value?.focus()
  store.loadLatest().catch(() => {
    store.latest ??= []
    toast("Couldn't load notifications.", 'error')
  })
}

function close(focusTrigger = false) {
  open.value = false
  if (focusTrigger) trigger.value?.focus()
}

function opened(n: AppNotification) {
  void store.markRead(n)
  close()
}

async function accept(n: AppNotification, inviteId: string) {
  accepting.value = n.id
  try {
    const boardId = await store.acceptInvite(n, inviteId)
    close()
    await router.push({ name: 'board', params: { boardId } })
  } catch (e) {
    toast(
      e instanceof ApiError && e.status === 410
        ? 'This invite has expired or was already used.'
        : "Couldn't accept the invite.",
      'error',
    )
  } finally {
    accepting.value = null
  }
}

function onOutside(event: MouseEvent) {
  const target = event.target as Node
  if (open.value && !panel.value?.contains(target) && !trigger.value?.contains(target)) close()
}

onMounted(() => document.addEventListener('click', onOutside))
onBeforeUnmount(() => document.removeEventListener('click', onOutside))
</script>

<template>
  <div class="relative">
    <button
      ref="trigger"
      type="button"
      class="ghost relative flex h-[42px] w-[42px] items-center justify-center rounded-full"
      :aria-label="label"
      :aria-expanded="open"
      aria-haspopup="dialog"
      aria-controls="notification-panel"
      @click="toggle"
    >
      <svg
        width="20"
        height="20"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path d="M6 8a6 6 0 1112 0c0 7 3 9 3 9H3s3-2 3-9" />
        <path d="M10 21h4" />
      </svg>
      <span
        v-if="badge"
        :key="popKey"
        data-testid="bell-badge"
        class="pop absolute -top-0.5 -right-0.5 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-accent px-1 text-[11px] font-bold text-white ring-2 ring-panel"
        aria-hidden="true"
        >{{ badge }}</span
      >
    </button>

    <div
      v-if="open"
      id="notification-panel"
      ref="panel"
      role="dialog"
      aria-label="Notifications"
      tabindex="-1"
      class="fade-up absolute top-12 right-0 z-40 flex w-[min(380px,calc(100vw-32px))] flex-col rounded-[18px] border border-line bg-panel shadow-[0_10px_24px_rgb(17_24_39/0.10)] outline-none"
      @keydown.esc.stop="close(true)"
    >
      <div class="flex items-center gap-2 border-b border-[#EEF1F6] px-4 py-3">
        <h2 class="text-[15px] font-semibold">Notifications</h2>
        <button
          v-if="store.unreadCount"
          type="button"
          class="ghost ml-auto h-8 rounded-full px-3 text-[13px] font-semibold text-accent-strong"
          @click="store.markAllRead()"
        >
          Mark all read
        </button>
      </div>

      <div v-if="store.latest === null" class="flex flex-col gap-3 p-4" aria-busy="true">
        <div v-for="i in 3" :key="i" class="flex items-center gap-3">
          <SkeletonBlock height="32px" width="32px" />
          <SkeletonBlock height="14px" width="70%" />
        </div>
      </div>
      <div v-else-if="!store.latest.length" class="flex flex-col items-center gap-1.5 px-4 py-10">
        <span class="text-3xl" aria-hidden="true">🌱</span>
        <p class="text-sm text-muted">You're all caught up.</p>
      </div>
      <ol v-else class="flex max-h-[420px] flex-col overflow-y-auto">
        <li
          v-for="n in store.latest"
          :key="n.id"
          class="border-b border-[#EEF1F6] px-4 py-3 hover:bg-hover"
          :class="{ 'bg-[#F6F8FD]': !n.readAt }"
        >
          <NotificationRow
            :notification="n"
            :when="`${dayLabel(n.createdAt)} · ${timeLabel(n.createdAt)}`"
            :accepting="accepting === n.id"
            @open="opened(n)"
            @accept="(id) => accept(n, id)"
          />
        </li>
      </ol>

      <RouterLink
        to="/notifications"
        class="ghost m-2 flex h-10 items-center justify-center rounded-full text-sm font-semibold no-underline"
        @click="close()"
        >See all notifications</RouterLink
      >
    </div>
  </div>
</template>
