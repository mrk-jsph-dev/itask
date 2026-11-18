<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import { renderMarkdown } from '@/shared/ui/markdown'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { activityApi, type ActivityEntry } from '../api'
import { timeLabel, dayLabel } from '../format'
import ActivityRow from './ActivityRow.vue'

/** The fields of a card comment the mixed timeline shows. */
export interface TimelineComment {
  id: string
  authorName: string
  authorColor: string
  body: string
  createdAt: string
}

/**
 * A card's history, newest first. With `comments`, they are interleaved with the activity by time
 * and the `comment.*` entries are left out, since the comments themselves are shown.
 */
const props = defineProps<{ boardId: string; cardId: string; comments?: TimelineComment[] }>()

const entries = ref<ActivityEntry[] | null>(null)
const nextCursor = ref<string | null>(null)
const failed = ref(false)
const loadingMore = ref(false)

type Row =
  | { kind: 'activity'; id: string; createdAt: string; entry: ActivityEntry }
  | { kind: 'comment'; id: string; createdAt: string; comment: TimelineComment }

const rows = computed<Row[]>(() => {
  const activity: Row[] = (entries.value ?? [])
    .filter((e) => !props.comments || !e.type.startsWith('comment.'))
    .map((entry) => ({ kind: 'activity', id: entry.id, createdAt: entry.createdAt, entry }))
  const comments: Row[] = (props.comments ?? []).map((comment) => ({
    kind: 'comment',
    id: comment.id,
    createdAt: comment.createdAt,
    comment,
  }))
  return [...activity, ...comments].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
})

watch(
  () => [props.boardId, props.cardId],
  async ([boardId, cardId]) => {
    entries.value = null
    failed.value = false
    try {
      const page = await activityApi.card(boardId!, cardId!)
      entries.value = page.items
      nextCursor.value = page.nextCursor
    } catch {
      entries.value = []
      failed.value = true
    }
  },
  { immediate: true },
)

async function older() {
  if (!nextCursor.value) return
  loadingMore.value = true
  try {
    const page = await activityApi.card(props.boardId, props.cardId, nextCursor.value)
    entries.value = [...(entries.value ?? []), ...page.items]
    nextCursor.value = page.nextCursor
  } catch {
    failed.value = true
  } finally {
    loadingMore.value = false
  }
}
</script>

<template>
  <div class="flex flex-col gap-3">
    <div v-if="entries === null" class="flex flex-col gap-3" aria-busy="true">
      <SkeletonBlock v-for="i in 3" :key="i" height="28px" />
    </div>
    <p v-else-if="failed" class="text-sm text-muted">Couldn't load the card's activity.</p>
    <p v-else-if="!rows.length" class="text-sm text-muted">No activity yet.</p>
    <ol v-else class="flex flex-col gap-3" aria-label="Card activity">
      <li v-for="row in rows" :key="row.id" class="fade-up">
        <ActivityRow v-if="row.kind === 'activity'" :entry="row.entry" compact />
        <div v-else class="flex items-start gap-3">
          <UserAvatar :name="row.comment.authorName" :color="row.comment.authorColor" :size="28" />
          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <p class="text-sm">
              <span aria-hidden="true">💬 </span>
              <span class="font-semibold">{{ row.comment.authorName }}</span> commented
            </p>
            <!-- eslint-disable-next-line vue/no-v-html -- sanitised by renderMarkdown -->
            <div class="md-body text-[15px]" v-html="renderMarkdown(row.comment.body)" />
            <p class="text-xs text-muted">
              <time :datetime="row.comment.createdAt"
                >{{ dayLabel(row.comment.createdAt) }} ·
                {{ timeLabel(row.comment.createdAt) }}</time
              >
            </p>
          </div>
        </div>
      </li>
    </ol>
    <button
      v-if="nextCursor"
      type="button"
      class="ghost w-fit rounded-full px-3 py-1 text-sm font-semibold text-muted"
      :disabled="loadingMore"
      @click="older"
    >
      {{ loadingMore ? 'Loading…' : 'Show older activity' }}
    </button>
  </div>
</template>
