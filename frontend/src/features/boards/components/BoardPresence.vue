<script setup lang="ts">
import { computed } from 'vue'

import type { LiveStatus, PresenceUser } from '@/shared/realtime/boardHub'
import UserAvatar from '@/shared/ui/UserAvatar.vue'

/** Who else has the board open, and whether live updates are reaching this tab. */
const props = defineProps<{ users: PresenceUser[]; meId: string | null; status: LiveStatus }>()

const others = computed(() => props.users.filter((u) => u.userId !== props.meId))
const shown = computed(() => others.value.slice(0, 3))
const extra = computed(() => others.value.length - shown.value.length)
const label = computed(() => {
  const count = others.value.length + 1
  const names = others.value.map((u) => u.displayName).join(', ')
  return `${count} people here: you, ${names}`
})

const notice = computed(() =>
  props.status === 'reconnecting'
    ? { text: 'Reconnecting…', title: 'Live updates will resume when the connection is back.' }
    : props.status === 'offline'
      ? { text: 'Offline', title: "Changes by others won't show until you reconnect." }
      : null,
)
</script>

<template>
  <span
    v-if="notice"
    class="flex items-center gap-1.5 rounded-full bg-hover px-2.5 py-1 text-xs font-semibold text-muted"
    role="status"
    :title="notice.title"
    data-testid="live-status"
  >
    <span class="h-1.5 w-1.5 rounded-full bg-warn-fill" aria-hidden="true" />{{ notice.text }}
  </span>
  <span
    v-else-if="status === 'live' && others.length"
    class="flex items-center gap-2"
    role="status"
    :title="label"
    data-testid="presence"
  >
    <span class="flex -space-x-2" aria-hidden="true">
      <UserAvatar
        v-for="(user, i) in shown"
        :key="user.userId"
        class="fade-up ring-2 ring-panel"
        :style="{ '--i': i }"
        :name="user.displayName"
        :color="user.avatarColor"
        :size="26"
      />
      <span
        v-if="extra > 0"
        class="fade-up flex h-[26px] min-w-[26px] items-center justify-center rounded-full bg-hover px-1 text-[11px] font-semibold text-muted ring-2 ring-panel"
        >+{{ extra }}</span
      >
    </span>
    <span class="text-xs font-semibold text-muted">
      {{ others.length + 1 }} people here<span class="sr-only"
        >: you, {{ others.map((u) => u.displayName).join(', ') }}</span
      >
    </span>
  </span>
</template>
