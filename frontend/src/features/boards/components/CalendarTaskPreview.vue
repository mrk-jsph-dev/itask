<script setup lang="ts">
import { computed, onMounted, ref, useTemplateRef, watch } from 'vue'

import { markdownExcerpt } from '@/shared/ui/markdown'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import type { BoardList, Card } from '../api'
import { dueState, priorityStyle } from '../cardOptions'
import { useViewsStore } from '../viewsStore'
import BlockedChip from './BlockedChip.vue'

const props = defineProps<{
  card: Card
  list: Pick<BoardList, 'name' | 'emoji'> | undefined
  editable: boolean
}>()
const emit = defineEmits<{ close: []; open: []; toggleComplete: [] }>()

const views = useViewsStore()
const panel = useTemplateRef('panel')

/** Description excerpt; undefined while loading, '' when there is none. */
const excerpt = ref<string | undefined>(undefined)

watch(
  () => props.card.id,
  async (id) => {
    excerpt.value = undefined
    try {
      const detail = await views.detail(id)
      if (props.card.id === id)
        excerpt.value = detail.description ? markdownExcerpt(detail.description) : ''
    } catch {
      if (props.card.id === id) excerpt.value = ''
    }
  },
  { immediate: true },
)

onMounted(() => panel.value?.focus())

const due = computed(() => {
  if (!props.card.dueAt) return null
  const date = new Date(props.card.dueAt)
  return {
    text: date.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' }),
    time: date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' }),
    state: dueState(props.card.dueAt, props.card.completedAt),
  }
})
const priority = computed(() =>
  props.card.priority === 'None' ? null : priorityStyle[props.card.priority],
)

function onKeydown(event: KeyboardEvent) {
  if (event.key !== 'Escape') return
  event.stopPropagation()
  emit('close')
}
</script>

<template>
  <div
    ref="panel"
    role="dialog"
    :aria-label="`Preview of ${card.title}`"
    tabindex="-1"
    class="preview pop flex flex-col gap-3.5 rounded-[20px] border border-line-soft bg-panel p-5 shadow-[0_20px_48px_rgb(17_24_39/0.16)] outline-none"
    @keydown="onKeydown"
  >
    <div class="flex items-center gap-2">
      <span class="flex min-w-0 flex-wrap gap-1.5">
        <span
          v-for="label in card.labels.slice(0, 2)"
          :key="label.id"
          class="label-chip rounded-full px-2.5 py-1 text-xs font-semibold"
          :style="{ '--tone': label.color }"
          ><span v-if="label.emoji" aria-hidden="true">{{ label.emoji }} </span
          >{{ label.name }}</span
        >
        <span
          v-if="!card.labels.length"
          class="rounded-full bg-hover px-2.5 py-1 text-xs font-semibold text-muted"
          >No label</span
        >
      </span>
      <span
        v-if="due"
        class="ml-auto flex shrink-0 items-center gap-1.5 text-xs font-semibold"
        :class="due.state === 'overdue' ? 'text-red-700' : 'text-muted'"
      >
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M3 5h18v16H3zM16 3v4M8 3v4M3 10h18" />
        </svg>
        {{ due.text }} · {{ due.time }}
      </span>
      <button
        type="button"
        class="ghost -mr-2 flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-muted"
        :class="{ 'ml-auto': !due }"
        aria-label="Close preview"
        @click="emit('close')"
      >
        ✕
      </button>
    </div>

    <div class="flex items-start gap-3">
      <button
        type="button"
        class="check press mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full border-2"
        :class="
          card.completedAt
            ? 'border-[#16A34A] bg-[#16A34A] text-white'
            : 'border-line text-transparent'
        "
        :disabled="!editable"
        :aria-pressed="!!card.completedAt"
        :aria-label="card.completedAt ? 'Mark as not done' : 'Mark as done'"
        @click="emit('toggleComplete')"
      >
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="3"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M5 12l5 5L20 7" />
        </svg>
      </button>
      <div class="flex min-w-0 flex-col gap-1">
        <h3
          class="text-lg leading-snug font-semibold break-words"
          :class="{ 'text-muted line-through': card.completedAt }"
        >
          <span v-if="card.emoji" aria-hidden="true">{{ card.emoji }} </span>{{ card.title }}
        </h3>
        <span class="text-xs text-muted">
          {{ card.key }}<template v-if="list"> · {{ list.emoji }} {{ list.name }}</template>
        </span>
      </div>
    </div>

    <div class="min-h-10 pl-10 text-sm text-[#374151]">
      <div v-if="excerpt === undefined" class="flex flex-col gap-1.5">
        <SkeletonBlock height="12px" />
        <SkeletonBlock height="12px" width="70%" />
      </div>
      <p v-else-if="excerpt" class="break-words">{{ excerpt }}</p>
      <p v-else class="text-muted italic">No description.</p>
    </div>

    <div class="flex flex-wrap items-center gap-3 pl-10 text-xs text-muted">
      <BlockedChip :card="card" show-blocking />
      <span
        v-if="priority"
        class="rounded-full px-2 py-0.5 font-semibold"
        :style="{ color: priority.color, backgroundColor: priority.bg }"
        >{{ card.priority }}</span
      >
      <span v-if="card.checklistTotal" class="flex items-center gap-1"
        >☑ {{ card.checklistDone }}/{{ card.checklistTotal }}</span
      >
      <span v-if="card.commentCount" class="flex items-center gap-1"
        >💬 {{ card.commentCount }}</span
      >
      <span
        v-if="card.assignees.length"
        class="flex -space-x-1.5"
        :aria-label="`Assigned to ${card.assignees.map((a) => a.displayName).join(', ')}`"
      >
        <UserAvatar
          v-for="a in card.assignees.slice(0, 4)"
          :key="a.userId"
          :name="a.displayName"
          :color="a.avatarColor"
          :size="22"
          class="ring-2 ring-white"
        />
      </span>
      <button
        type="button"
        class="press ml-auto h-9 rounded-full bg-accent px-4 text-sm font-semibold text-white"
        @click="emit('open')"
      >
        Open task
      </button>
    </div>
  </div>
</template>

<style scoped>
.preview {
  width: min(340px, calc(100vw - 32px));
}

.label-chip {
  background: color-mix(in srgb, var(--tone) 14%, white);
  color: color-mix(in srgb, var(--tone) 75%, #111827);
}

.check {
  transition:
    background-color var(--d-base) var(--ease-out),
    border-color var(--d-base) var(--ease-out),
    color var(--d-base) var(--ease-out);
}

.check:not(:disabled):not([aria-pressed='true']):hover {
  border-color: #16a34a;
  color: #16a34a;
}
</style>
