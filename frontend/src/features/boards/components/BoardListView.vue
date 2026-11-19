<script setup lang="ts">
import { computed } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import type { BoardList, Card, CardPatch, CardPriority, Label, Member } from '../api'
import { moveToDay } from '../calendar'
import {
  dueState,
  formatDue,
  fromDateInput,
  priorities,
  priorityStyle,
  toDateInput,
} from '../cardOptions'
import { groupCards, type GroupKey } from '../filters'

const props = defineProps<{
  /** Filtered, sorted cards; null while loading. */
  cards: Card[] | null
  truncated: boolean
  group: GroupKey
  lists: Pick<BoardList, 'id' | 'name' | 'emoji'>[]
  labels: Label[]
  members: Pick<Member, 'userId' | 'displayName'>[]
  editable: boolean
}>()
const emit = defineEmits<{ open: [cardId: string]; update: [cardId: string, patch: CardPatch] }>()

const listById = computed(() => new Map(props.lists.map((l) => [l.id, l])))
const groups = computed(() =>
  props.cards
    ? groupCards(props.cards, props.group, {
        lists: props.lists,
        labels: props.labels,
        members: props.members,
      })
    : [],
)

function setDue(card: Card, value: string) {
  const dueAt = !value
    ? null
    : card.dueAt
      ? moveToDay(card.dueAt, value)
      : fromDateInput(value, 'end')
  if (dueAt !== card.dueAt) emit('update', card.id, { dueAt })
}

function setPriority(card: Card, value: string) {
  if (value !== card.priority) emit('update', card.id, { priority: value as CardPriority })
}

const dueClass = (card: Card) => {
  const state = card.dueAt ? dueState(card.dueAt, card.completedAt) : null
  return state === 'overdue'
    ? 'text-red-700 font-semibold'
    : state === 'soon'
      ? 'text-amber-700'
      : ''
}
</script>

<template>
  <div
    v-if="cards === null"
    class="flex flex-col gap-2"
    aria-busy="true"
    aria-label="Loading tasks"
  >
    <SkeletonBlock v-for="i in 6" :key="i" height="44px" />
  </div>

  <p v-else-if="cards.length === 0" class="py-10 text-center text-sm text-muted">
    🔍 No tasks match these filters.
  </p>

  <div v-else class="flex flex-col gap-6">
    <p
      v-if="truncated"
      role="status"
      class="rounded-2xl bg-amber-50 px-4 py-3 text-sm text-amber-900"
    >
      Showing the first {{ cards.length }} tasks. Narrow the filter to see the rest.
    </p>
    <section
      v-for="(section, s) in groups"
      :key="section.key"
      class="fade-up flex flex-col gap-2"
      :style="{ '--i': Math.min(s, 8) }"
      :aria-label="group === 'none' ? 'Tasks' : `${section.title} tasks`"
    >
      <h2 v-if="group !== 'none'" class="flex items-center gap-2 px-1 text-base font-semibold">
        <span
          v-if="section.color"
          class="h-2.5 w-2.5 rounded-full"
          :style="{ backgroundColor: section.color }"
          aria-hidden="true"
        />
        <span v-if="section.emoji" aria-hidden="true">{{ section.emoji }}</span>
        {{ section.title }}
        <span class="text-[13px] font-normal text-muted">{{ section.cards.length }}</span>
      </h2>
      <table class="task-table w-full border-separate border-spacing-0 text-sm">
        <thead>
          <tr>
            <th scope="col" class="w-20">Key</th>
            <th scope="col">Title</th>
            <th scope="col" class="w-36">List</th>
            <th scope="col" class="w-44">Labels</th>
            <th scope="col" class="w-28">Assignees</th>
            <th scope="col" class="w-40">Due</th>
            <th scope="col" class="w-32">Priority</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="card in section.cards" :key="card.id" :data-card-row="card.id">
            <td data-label="Key" class="text-muted tabular-nums">{{ card.key }}</td>
            <td data-label="Title" class="title-cell">
              <button
                type="button"
                class="ghost -mx-2 flex max-w-full items-center gap-1.5 rounded-lg px-2 py-1 text-left font-semibold"
                :class="{ 'text-muted line-through': card.completedAt }"
                @click="emit('open', card.id)"
              >
                <span v-if="card.emoji" aria-hidden="true">{{ card.emoji }}</span>
                <span class="min-w-0 break-words">{{ card.title }}</span>
              </button>
            </td>
            <td data-label="List">
              <span class="inline-flex items-center gap-1">
                <span v-if="listById.get(card.listId)?.emoji" aria-hidden="true">{{
                  listById.get(card.listId)?.emoji
                }}</span>
                {{ listById.get(card.listId)?.name }}
              </span>
            </td>
            <td data-label="Labels">
              <span class="flex flex-wrap gap-1">
                <span
                  v-for="label in card.labels"
                  :key="label.id"
                  class="rounded-full px-2 py-0.5 text-xs font-semibold text-white"
                  :style="{ backgroundColor: label.color }"
                  >{{ label.emoji }} {{ label.name }}</span
                >
              </span>
            </td>
            <td data-label="Assignees">
              <span
                class="flex -space-x-1.5"
                :aria-label="card.assignees.map((a) => a.displayName).join(', ') || 'Nobody'"
              >
                <UserAvatar
                  v-for="a in card.assignees.slice(0, 4)"
                  :key="a.userId"
                  :name="a.displayName"
                  :color="a.avatarColor"
                  :size="24"
                  class="ring-2 ring-white"
                />
              </span>
            </td>
            <td data-label="Due">
              <input
                v-if="editable"
                type="date"
                class="h-8 rounded-lg border border-transparent bg-transparent px-1.5 hover:border-line focus:border-accent"
                :class="dueClass(card)"
                :value="toDateInput(card.dueAt)"
                :aria-label="`Due date of ${card.title}`"
                @change="setDue(card, ($event.target as HTMLInputElement).value)"
              />
              <span v-else-if="card.dueAt" :class="dueClass(card)">{{
                formatDue(card.dueAt)
              }}</span>
            </td>
            <td data-label="Priority">
              <select
                v-if="editable"
                class="h-8 rounded-lg border border-transparent bg-transparent px-1 hover:border-line focus:border-accent"
                :value="card.priority"
                :aria-label="`Priority of ${card.title}`"
                @change="setPriority(card, ($event.target as HTMLSelectElement).value)"
              >
                <option v-for="p in priorities" :key="p" :value="p">
                  {{ p === 'None' ? '—' : p }}
                </option>
              </select>
              <span
                v-else-if="card.priority !== 'None'"
                class="rounded-full px-2 py-0.5 text-xs font-semibold"
                :style="{
                  color: priorityStyle[card.priority].color,
                  backgroundColor: priorityStyle[card.priority].bg,
                }"
                >{{ card.priority }}</span
              >
            </td>
          </tr>
        </tbody>
      </table>
    </section>
  </div>
</template>

<style scoped>
.task-table th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--color-panel);
  border-bottom: 1px solid var(--color-line-soft);
  padding: 8px 10px;
  text-align: left;
  font-size: 12px;
  font-weight: 600;
  color: var(--color-muted);
}

.task-table td {
  border-bottom: 1px solid var(--color-line-soft);
  padding: 6px 10px;
  vertical-align: middle;
}

.task-table tbody tr {
  transition: background-color var(--d-fast) var(--ease-out);
}

.task-table tbody tr:hover {
  background: var(--color-hover);
}

/* Phones: each row becomes a stacked card so the page never scrolls sideways. */
@media (max-width: 767px) {
  .task-table thead {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip: rect(0 0 0 0);
  }

  .task-table,
  .task-table tbody,
  .task-table tr,
  .task-table td {
    display: block;
  }

  .task-table tr {
    border: 1px solid var(--color-line-soft);
    border-radius: 14px;
    padding: 6px 4px;
    margin-bottom: 8px;
  }

  .task-table td {
    display: flex;
    align-items: center;
    gap: 12px;
    border: 0;
    padding: 3px 10px;
    min-height: 0;
  }

  .task-table td:empty {
    display: none;
  }

  .task-table td::before {
    content: attr(data-label);
    flex: 0 0 72px;
    font-size: 12px;
    font-weight: 600;
    color: var(--color-muted);
  }

  .task-table td.title-cell::before {
    content: none;
  }
}
</style>
