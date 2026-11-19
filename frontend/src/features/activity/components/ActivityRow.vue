<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

import UserAvatar from '@/shared/ui/UserAvatar.vue'
import type { ActivityEntry } from '../api'
import { dayLabel, describeActivity, timeLabel } from '../format'

/** `showBoard`: add the board name before the time (personal feed). */
const props = withDefaults(
  defineProps<{ entry: ActivityEntry; showBoard?: boolean; compact?: boolean }>(),
  { showBoard: false, compact: false },
)

const line = computed(() => describeActivity(props.entry))
/** Feeds group by day already; the card sheet's short list does not. */
const when = computed(() =>
  props.compact
    ? `${dayLabel(props.entry.createdAt)} · ${timeLabel(props.entry.createdAt)}`
    : timeLabel(props.entry.createdAt),
)
const cardLink = computed(() =>
  props.entry.cardId && !props.compact
    ? { name: 'card', params: { boardId: props.entry.board.id, cardId: props.entry.cardId } }
    : null,
)
</script>

<template>
  <div class="flex items-start gap-3">
    <UserAvatar
      :name="entry.actor.displayName"
      :color="entry.actor.avatarColor"
      :size="compact ? 28 : 32"
    />
    <div class="flex min-w-0 flex-1 flex-col gap-0.5">
      <p class="text-sm leading-[1.45]">
        <span aria-hidden="true">{{ line.emoji }} </span>
        <span class="font-semibold">{{ entry.actor.displayName }}</span>
        {{ line.what }}
        <template v-if="line.target">
          <RouterLink v-if="cardLink" :to="cardLink" class="font-semibold no-underline">{{
            line.target
          }}</RouterLink>
          <span v-else class="font-semibold">{{ line.target }}</span>
        </template>
      </p>
      <p class="text-xs text-muted">
        <template v-if="showBoard">
          {{ entry.board.emoji ? `${entry.board.emoji} ` : '' }}{{ entry.board.name }} ·
        </template>
        <time :datetime="entry.createdAt">{{ when }}</time>
      </p>
    </div>
  </div>
</template>
