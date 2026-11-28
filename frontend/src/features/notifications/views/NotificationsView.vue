<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { ApiError } from '@/shared/api/http'
import PanelHeader from '@/shared/ui/PanelHeader.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { toast } from '@/shared/ui/toast'
import { useAuthStore } from '@/features/auth/store'
import type { AppNotification } from '../api'
import NotificationRow from '../components/NotificationRow.vue'
import { useNotificationsStore } from '../store'

const store = useNotificationsStore()
const auth = useAuthStore()
const router = useRouter()
const accepting = ref<string | null>(null)

const chips = [
  { label: 'All', unread: false },
  { label: 'Unread', unread: true },
] as const

async function run(action: () => Promise<void>, fallback: string) {
  try {
    await action()
  } catch {
    toast(fallback, 'error')
  }
}

onMounted(() => run(() => store.load(false), "Couldn't load notifications."))

const pick = (unread: boolean) => run(() => store.load(unread), "Couldn't load notifications.")
const more = () => run(() => store.loadMore(), "Couldn't load older notifications.")

async function accept(n: AppNotification, inviteId: string) {
  accepting.value = n.id
  try {
    const boardId = await store.acceptInvite(n, inviteId)
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
</script>

<template>
  <PanelHeader title="Notifications" :user="auth.user" />

  <section
    class="flex w-full max-w-3xl flex-col gap-3.5 px-5 py-6 md:px-7"
    aria-label="Notifications"
  >
    <div class="flex flex-wrap items-center gap-2.5">
      <div
        role="group"
        aria-label="Filter notifications"
        class="flex gap-1 rounded-[14px] bg-hover p-1"
      >
        <button
          v-for="chip in chips"
          :key="chip.label"
          type="button"
          :aria-pressed="store.unreadOnly === chip.unread"
          class="h-[34px] rounded-[10px] px-3 text-sm"
          :class="
            store.unreadOnly === chip.unread
              ? 'bg-panel font-semibold text-ink shadow-card'
              : 'font-medium text-ink-soft'
          "
          @click="pick(chip.unread)"
        >
          {{ chip.label }}
        </button>
      </div>
      <button
        type="button"
        class="ghost ml-auto h-10 rounded-full border border-line px-4 text-sm font-semibold disabled:opacity-60"
        :disabled="!store.unreadCount"
        @click="store.markAllRead()"
      >
        Mark all read
      </button>
    </div>

    <div
      v-if="store.items === null"
      class="flex flex-col gap-4 rounded-[18px] border-[1.5px] border-dashed border-line px-3.5 py-4"
      aria-busy="true"
      aria-label="Loading notifications"
    >
      <div v-for="i in 5" :key="i" class="flex items-center gap-3">
        <SkeletonBlock height="32px" width="32px" />
        <div class="flex flex-1 flex-col gap-1.5">
          <SkeletonBlock height="14px" width="70%" />
          <SkeletonBlock height="12px" width="30%" />
        </div>
      </div>
    </div>

    <div
      v-else-if="!store.items.length"
      class="fade-up flex flex-col items-center gap-2 rounded-[18px] border-[1.5px] border-dashed border-line py-16 text-center"
    >
      <span class="text-4xl" aria-hidden="true">🌱</span>
      <p class="px-4 text-muted">
        {{
          store.unreadOnly
            ? "You're all caught up."
            : 'No notifications yet. Mentions, assignments and due dates show up here.'
        }}
      </p>
    </div>

    <template v-else>
      <section
        v-for="(group, g) in store.groups"
        :key="group.day"
        class="fade-up flex flex-col rounded-[18px] border-[1.5px] border-dashed border-line px-3.5 py-2"
        :style="{ '--i': Math.min(g, 8) }"
        :aria-label="group.day"
      >
        <h2 class="pt-2 pb-1 text-[13px] font-semibold text-muted">{{ group.day }}</h2>
        <ol class="flex flex-col">
          <li v-for="n in group.items" :key="n.id" class="border-t border-line-faint py-2.5">
            <NotificationRow
              :notification="n"
              :accepting="accepting === n.id"
              @open="store.markRead(n)"
              @accept="(id) => accept(n, id)"
            />
          </li>
        </ol>
      </section>
      <button
        v-if="store.nextCursor"
        type="button"
        class="ghost flex h-10 w-fit items-center gap-2 self-center rounded-full border border-line px-5 text-sm font-semibold"
        :disabled="store.loadingMore"
        @click="more"
      >
        <span v-if="store.loadingMore" class="spinner" aria-hidden="true" />
        {{ store.loadingMore ? 'Loading…' : 'Show older notifications' }}
      </button>
    </template>
  </section>
</template>
