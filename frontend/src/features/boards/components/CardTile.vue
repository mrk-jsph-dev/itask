<script setup lang="ts">
import { computed, ref, useId, useTemplateRef } from 'vue'

import { initials } from '@/shared/ui/initials'
import { useMenu } from '@/shared/ui/menu'
import type { BoardList, Card } from '../api'
import { dueState, formatDue, priorityStyle } from '../cardOptions'

const props = defineProps<{
  card: Card
  editable: boolean
  canMoveUp: boolean
  canMoveDown: boolean
  /** Lists the card can move to (all active lists but its own). */
  targets: Pick<BoardList, 'id' | 'name' | 'emoji'>[]
}>()
const emit = defineEmits<{
  open: []
  move: [delta: -1 | 1]
  moveTo: [listId: string]
  toggleComplete: []
  archive: []
}>()

const menuId = useId()
const menu = useTemplateRef('menu')
const trigger = useTemplateRef('trigger')
const { open: menuOpen, close: closeMenu, toggle, focusFirst, onKeydown } = useMenu(menu, trigger)
/** The menu shows the list picker instead of the actions. */
const picking = ref(false)

const shownAssignees = computed(() => props.card.assignees.slice(0, 3))
const extraAssignees = computed(() => props.card.assignees.length - shownAssignees.value.length)
const due = computed(() =>
  props.card.dueAt ? dueState(props.card.dueAt, props.card.completedAt) : null,
)
const priority = computed(() =>
  props.card.priority === 'None' ? null : priorityStyle[props.card.priority],
)

async function openMenu() {
  picking.value = false
  await toggle()
}

async function showPicker() {
  picking.value = true
  await focusFirst()
}

function pick(action: 'open' | 'up' | 'down' | 'complete' | 'archive') {
  closeMenu(action === 'up' || action === 'down')
  if (action === 'open') emit('open')
  else if (action === 'up') emit('move', -1)
  else if (action === 'down') emit('move', 1)
  else if (action === 'complete') emit('toggleComplete')
  else emit('archive')
}

function pickList(listId: string) {
  closeMenu(true)
  emit('moveTo', listId)
}

const itemClass = 'ghost rounded-lg px-3 py-2 text-left disabled:opacity-40'
</script>

<template>
  <article
    class="lift relative flex flex-col gap-3 rounded-[14px] border border-line-soft bg-panel p-3.5 shadow-[0_1px_2px_rgb(17_24_39/0.04)]"
    :class="{ 'cursor-grab active:cursor-grabbing': editable }"
    :aria-label="`${card.key} ${card.title}`"
  >
    <span
      v-if="card.coverColor"
      class="absolute inset-x-0 top-0 h-1.5 rounded-t-[13px]"
      :style="{ backgroundColor: card.coverColor }"
      aria-hidden="true"
    />
    <div class="flex items-start gap-2">
      <button
        type="button"
        class="card-open min-w-0 flex-1 text-left after:absolute after:inset-0 after:content-['']"
        @click="emit('open')"
      >
        <span class="block text-xs font-semibold tracking-wide text-muted">{{ card.key }}</span>
        <span
          class="mt-1 block text-[15px] leading-snug font-medium break-words"
          :class="{ 'text-muted line-through': card.completedAt }"
        >
          <span v-if="card.completedAt" class="mr-1 text-emerald-600 no-underline" aria-label="Done"
            >✓</span
          >{{ card.title }}
        </span>
      </button>
      <template v-if="editable">
        <button
          ref="trigger"
          type="button"
          class="ghost relative z-10 -mt-1 -mr-1 h-7 w-7 shrink-0 rounded-lg font-bold text-muted"
          aria-haspopup="menu"
          :aria-expanded="menuOpen"
          :aria-controls="menuId"
          :aria-label="`${card.title} card menu`"
          @click.stop="openMenu"
        >
          ···
        </button>
        <div
          v-if="menuOpen"
          :id="menuId"
          ref="menu"
          role="menu"
          :aria-label="picking ? 'Move to list' : `${card.title} card actions`"
          class="pop absolute top-10 right-2 z-30 flex w-48 flex-col rounded-[var(--radius-control)] border border-line-soft bg-panel p-1 text-sm shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
          @keydown="onKeydown"
        >
          <template v-if="picking">
            <button
              v-for="target in targets"
              :key="target.id"
              type="button"
              role="menuitem"
              :class="itemClass"
              @click="pickList(target.id)"
            >
              <span aria-hidden="true">{{ target.emoji }}</span> {{ target.name }}
            </button>
            <p v-if="targets.length === 0" class="px-3 py-2 text-muted">No other lists.</p>
          </template>
          <template v-else>
            <button type="button" role="menuitem" :class="itemClass" @click="pick('open')">
              📝 Open
            </button>
            <button
              type="button"
              role="menuitem"
              :class="itemClass"
              :disabled="!canMoveUp"
              @click="pick('up')"
            >
              ↑ Move up
            </button>
            <button
              type="button"
              role="menuitem"
              :class="itemClass"
              :disabled="!canMoveDown"
              @click="pick('down')"
            >
              ↓ Move down
            </button>
            <button type="button" role="menuitem" :class="itemClass" @click.stop="showPicker">
              → Move to list…
            </button>
            <button type="button" role="menuitem" :class="itemClass" @click="pick('complete')">
              {{ card.completedAt ? '↺ Reopen' : '✓ Mark complete' }}
            </button>
            <button
              type="button"
              role="menuitem"
              :class="[itemClass, 'text-red-700']"
              @click="pick('archive')"
            >
              🗄 Archive
            </button>
          </template>
        </div>
      </template>
    </div>

    <ul v-if="card.labels.length" class="flex flex-wrap gap-1.5" aria-label="Labels">
      <li
        v-for="label in card.labels"
        :key="label.id"
        class="flex items-center gap-1.5 rounded-full border border-line-soft px-2.5 py-0.5 text-[13px] text-[#374151]"
      >
        <span v-if="label.emoji" aria-hidden="true">{{ label.emoji }}</span>
        <span
          v-else
          class="h-2 w-2 rounded-full"
          :style="{ backgroundColor: label.color }"
          aria-hidden="true"
        />
        {{ label.name }}
      </li>
    </ul>

    <div
      v-if="
        priority || card.dueAt || card.assignees.length || card.checklistTotal || card.commentCount
      "
      class="flex flex-wrap items-center gap-2.5 border-t border-[#EEF1F6] pt-2.5 text-[13px]"
    >
      <span
        v-if="priority"
        class="flex items-center gap-1 rounded-lg px-2 py-0.5 font-semibold"
        :style="{ color: priority.color, backgroundColor: priority.bg }"
      >
        <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M5 3v18h2v-7h11l-2-4 2-4H7V3z" />
        </svg>
        {{ card.priority }}
      </span>
      <span v-if="card.checklistTotal" class="text-[#374151]" title="Checklist">
        ☑️ {{ card.checklistDone }}/{{ card.checklistTotal }}
      </span>
      <span v-if="card.commentCount" class="text-[#374151]" title="Comments">
        💬 {{ card.commentCount }}
      </span>
      <span v-if="card.assignees.length" class="ml-auto flex" aria-label="Assignees">
        <span
          v-for="(person, i) in shownAssignees"
          :key="person.userId"
          class="flex h-[26px] w-[26px] items-center justify-center rounded-full border-2 border-panel text-[10px] font-bold text-white"
          :class="{ '-ml-[7px]': i > 0 }"
          :style="{ backgroundColor: person.avatarColor }"
          :title="person.displayName"
          >{{ initials(person.displayName) }}</span
        >
        <span
          v-if="extraAssignees > 0"
          class="-ml-[7px] flex h-[26px] w-[26px] items-center justify-center rounded-full border-2 border-panel bg-hover text-[10px] font-bold text-muted"
          >+{{ extraAssignees }}</span
        >
      </span>
      <span
        v-if="card.dueAt && due"
        class="flex items-center gap-1 rounded-lg px-1.5 py-0.5"
        :class="[
          card.assignees.length ? '' : 'ml-auto',
          due === 'overdue'
            ? 'bg-red-50 font-semibold text-red-700'
            : due === 'soon'
              ? 'bg-amber-50 text-amber-800'
              : due === 'done'
                ? 'text-muted'
                : 'text-[#374151]',
        ]"
        :title="due === 'overdue' ? 'Overdue' : 'Due'"
      >
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v5l3 2" />
        </svg>
        <span
          ><span v-if="due === 'overdue'" class="sr-only">Overdue, </span
          >{{ formatDue(card.dueAt) }}</span
        >
      </span>
    </div>
  </article>
</template>
