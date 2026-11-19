<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import type { Card } from '../api'
import {
  addDays,
  addMonths,
  byDay,
  dayKey,
  monthGrid,
  monthKey,
  moveToDay,
  parseMonth,
  rangeOf,
  weekDays,
} from '../calendar'
import { dueState } from '../cardOptions'

const props = defineProps<{
  /** Cards due in the shown days; null while loading. */
  cards: Card[] | null
  /** Shown month as `yyyy-mm`; null = this month. */
  month: string | null
  editable: boolean
  /** Matching tasks with no due date (they can't appear on the grid). */
  undatedCount: number
}>()
const emit = defineEmits<{
  'update:month': [month: string | null]
  range: [range: { from: Date; to: Date }]
  open: [cardId: string]
  move: [cardId: string, dueAt: string]
  showUndated: []
}>()

/** Phones show one week instead of the month. */
const narrow = ref(false)
let media: MediaQueryList | null = null
const onMedia = () => (narrow.value = !!media?.matches)
onMounted(() => {
  media = window.matchMedia?.('(max-width: 767px)') ?? null
  onMedia()
  media?.addEventListener('change', onMedia)
})
onBeforeUnmount(() => media?.removeEventListener('change', onMedia))

const today = dayKey(new Date())
const shownMonth = computed(() => parseMonth(props.month))
/** Week mode's anchor day; starts at today, or the 1st when another month is in the URL. */
const weekAnchor = ref(
  props.month && props.month !== monthKey(new Date()) ? shownMonth.value : new Date(),
)

const days = computed(() =>
  narrow.value ? weekDays(weekAnchor.value) : monthGrid(shownMonth.value),
)
const byDate = computed(() => byDay(props.cards ?? []))

watch(
  days,
  (value, old) => {
    if (!old || dayKey(value[0]!) !== dayKey(old[0]!) || value.length !== old.length)
      emit('range', rangeOf(value))
  },
  { immediate: true },
)

const title = computed(() => {
  if (!narrow.value)
    return shownMonth.value.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
  const first = days.value[0]!
  const last = days.value[6]!
  const fmt = (d: Date) => d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  return `${fmt(first)} – ${fmt(last)}`
})

function setMonth(date: Date) {
  const key = monthKey(date)
  emit('update:month', key === monthKey(new Date()) ? null : key)
}

function step(delta: -1 | 1) {
  if (narrow.value) {
    weekAnchor.value = addDays(weekAnchor.value, delta * 7)
    setMonth(weekAnchor.value)
  } else {
    setMonth(addMonths(shownMonth.value, delta))
  }
}

function goToday() {
  weekAnchor.value = new Date()
  setMonth(new Date())
}

const weekdayNames = computed(() =>
  weekDays(new Date()).map((d) => d.toLocaleDateString(undefined, { weekday: 'short' })),
)

const dragging = ref<string | null>(null)
const over = ref<string | null>(null)

function onDragStart(event: DragEvent, card: Card) {
  if (!props.editable) return
  dragging.value = card.id
  event.dataTransfer?.setData('text/plain', card.id)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move'
}

function onDrop(event: DragEvent, day: string) {
  over.value = null
  const cardId = event.dataTransfer?.getData('text/plain') || dragging.value
  dragging.value = null
  const card = props.cards?.find((c) => c.id === cardId)
  if (card?.dueAt && dayKey(new Date(card.dueAt)) !== day)
    emit('move', card.id, moveToDay(card.dueAt, day))
}

/** Alt+arrows move a focused task by a day (left/right) or a week (up/down). */
function onChipKey(event: KeyboardEvent, card: Card) {
  if (!props.editable || !event.altKey || !card.dueAt) return
  const delta = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[event.key]
  if (!delta) return
  event.preventDefault()
  const target = addDays(new Date(card.dueAt), delta)
  emit('move', card.id, moveToDay(card.dueAt, dayKey(target)))
}

const chipClass = (card: Card) => {
  const state = card.dueAt ? dueState(card.dueAt, card.completedAt) : null
  if (state === 'done') return 'bg-[#F3F4F6] text-muted line-through'
  if (state === 'overdue') return 'bg-[#FEF2F2] text-red-800'
  return 'bg-[#EFF4FF] text-[#1E3A8A]'
}

const dayLabel = (d: Date) =>
  d.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="flex flex-wrap items-center gap-2">
      <h2 class="mr-auto text-lg font-semibold" aria-live="polite">{{ title }}</h2>
      <button
        type="button"
        class="ghost h-9 rounded-full border border-line px-3 text-sm font-semibold"
        @click="goToday"
      >
        Today
      </button>
      <button
        type="button"
        class="ghost h-9 w-9 rounded-full border border-line"
        :aria-label="narrow ? 'Previous week' : 'Previous month'"
        @click="step(-1)"
      >
        ‹
      </button>
      <button
        type="button"
        class="ghost h-9 w-9 rounded-full border border-line"
        :aria-label="narrow ? 'Next week' : 'Next month'"
        @click="step(1)"
      >
        ›
      </button>
    </div>

    <p v-if="undatedCount" class="text-sm text-muted">
      {{ undatedCount }} {{ undatedCount === 1 ? 'task has' : 'tasks have' }} no due date.
      <button type="button" class="font-semibold text-accent" @click="emit('showUndated')">
        Show in List
      </button>
    </p>
    <p v-if="editable" class="sr-only">
      Drag a task to another day, or focus it and press Alt with an arrow key.
    </p>

    <div v-if="cards === null" class="cal-grid" aria-busy="true" aria-label="Loading calendar">
      <SkeletonBlock v-for="i in narrow ? 7 : 35" :key="i" height="96px" />
    </div>

    <div v-else class="cal-grid" :class="{ week: narrow }" role="grid" :aria-label="title">
      <template v-if="!narrow">
        <div
          v-for="name in weekdayNames"
          :key="name"
          class="px-2 pb-1 text-xs font-semibold text-muted"
          role="columnheader"
        >
          {{ name }}
        </div>
      </template>
      <div
        v-for="(day, i) in days"
        :key="dayKey(day)"
        role="gridcell"
        :aria-label="dayLabel(day)"
        :data-day="dayKey(day)"
        class="day fade-up flex min-h-24 min-w-0 flex-col gap-1 rounded-[14px] border p-1.5"
        :class="[
          !narrow && day.getMonth() !== shownMonth.getMonth()
            ? 'bg-hover/60 text-muted'
            : 'bg-panel',
          over === dayKey(day) ? 'border-accent ring-2 ring-accent/20' : 'border-line-soft',
        ]"
        :style="{ '--i': Math.min(i % 7, 6) }"
        @dragover.prevent="editable && (over = dayKey(day))"
        @dragleave="over === dayKey(day) && (over = null)"
        @drop.prevent="onDrop($event, dayKey(day))"
      >
        <span
          class="flex h-6 items-center gap-1.5 px-1 text-xs font-semibold"
          :class="dayKey(day) === today ? 'text-accent' : ''"
        >
          <span v-if="narrow">{{ day.toLocaleDateString(undefined, { weekday: 'short' }) }}</span>
          <span :class="dayKey(day) === today ? 'rounded-full bg-accent px-1.5 text-white' : ''">{{
            day.getDate()
          }}</span>
        </span>
        <button
          v-for="card in byDate.get(dayKey(day)) ?? []"
          :key="card.id"
          type="button"
          :draggable="editable"
          :data-card-chip="card.id"
          class="press truncate rounded-lg px-2 py-1 text-left text-xs font-semibold"
          :class="[
            chipClass(card),
            { 'opacity-50': dragging === card.id, 'cursor-grab': editable },
          ]"
          :aria-label="`${card.key} ${card.title}`"
          :aria-keyshortcuts="
            editable ? 'Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown' : undefined
          "
          @click="emit('open', card.id)"
          @keydown="onChipKey($event, card)"
          @dragstart="onDragStart($event, card)"
          @dragend="dragging = null"
        >
          <span v-if="card.emoji" aria-hidden="true">{{ card.emoji }} </span>{{ card.title }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cal-grid {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 6px;
}

.cal-grid.week {
  grid-template-columns: 1fr;
}

.day {
  transition:
    border-color var(--d-fast) var(--ease-out),
    box-shadow var(--d-fast) var(--ease-out);
}
</style>
