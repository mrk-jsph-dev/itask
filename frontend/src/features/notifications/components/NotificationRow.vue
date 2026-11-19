<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

import { timeLabel } from '@/shared/ui/dates'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import type { AppNotification } from '../api'
import { describeNotification, inviteIdOf } from '../format'

/** `when`: the time text (the page groups by day; the dropdown passes day and time). */
const props = defineProps<{ notification: AppNotification; when?: string; accepting?: boolean }>()
const emit = defineEmits<{ open: []; accept: [inviteId: string] }>()

const line = computed(() => describeNotification(props.notification))
const inviteId = computed(() => inviteIdOf(props.notification))
const unread = computed(() => props.notification.readAt === null)
const time = computed(() => props.when ?? timeLabel(props.notification.createdAt))
</script>

<template>
  <div class="relative flex items-start gap-3">
    <UserAvatar
      v-if="notification.actor"
      :name="notification.actor.displayName"
      :color="notification.actor.avatarColor"
      :size="32"
    />
    <span
      v-else
      class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-hover text-base"
      aria-hidden="true"
      >{{ line.emoji }}</span
    >
    <div class="flex min-w-0 flex-1 flex-col gap-0.5">
      <p class="text-sm leading-[1.45]" :class="unread ? 'text-ink' : 'text-[#4B5563]'">
        <span v-if="notification.actor" aria-hidden="true">{{ line.emoji }} </span>
        <span v-if="line.actor" class="font-semibold">{{ line.actor }} </span>
        {{ line.what }}
        <RouterLink
          v-if="line.to && line.target"
          :to="line.to"
          class="font-semibold no-underline after:absolute after:inset-0"
          @click="emit('open')"
          >{{ line.target }}</RouterLink
        >
        <span v-else-if="line.target" class="font-semibold">{{ line.target }}</span
        >{{ line.suffix }}
      </p>
      <p class="text-xs text-muted">
        {{ notification.board.emoji ? `${notification.board.emoji} ` : ''
        }}{{ notification.board.name }} ·
        <time :datetime="notification.createdAt">{{ time }}</time>
      </p>
      <button
        v-if="inviteId && unread"
        type="button"
        class="press relative z-10 mt-1.5 h-8 w-fit rounded-full bg-accent px-4 text-[13px] font-semibold text-white disabled:opacity-60"
        :disabled="accepting"
        @click="emit('accept', inviteId)"
      >
        {{ accepting ? 'Joining…' : 'Accept invite' }}
      </button>
    </div>
    <span
      v-if="unread"
      class="mt-2 h-2 w-2 shrink-0 rounded-full bg-accent"
      role="img"
      aria-label="Unread"
    />
  </div>
</template>
