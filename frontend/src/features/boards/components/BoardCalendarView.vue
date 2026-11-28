<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import type { BoardList, Card } from '../api'
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
import { cardTone } from '../cardOptions'
import CalendarTaskPreview from './CalendarTaskPreview.vue'

const props = defineProps<{
  /** Cards due in the shown days; null while loading. */
  cards: Card[] | null
  /** Shown month as `yyyy-mm`; null = this month. */
  month: string | null
  editable: boolean
  /** Active lists, for the preview's list name. */
  lists: Pick<BoardList, 'id' | 'name' | 'emoji'>[]
  /** Matching tasks with no due date (they can't appear on the grid). */
  undatedCount: number
}>()
const emit = defineEmits<{
  'update:month': [month: string | null]
  range: [range: { from: Date; to: Date }]
  open: [cardId: string]
  move: [cardId: string, dueAt: string]
  toggleComplete: [cardId: string]
  showUndated: []
}>()

/** Tasks a month cell lists before folding the rest behind "+N more". */
const perCell = 3

/** Phones always show one week, stacked by day. */
const narrow = ref(false)
let media: MediaQueryList | null = null
const onMedia = () => (narrow.value = !!media?.matches)
onMounted(() => {
  media = window.matchMedia?.('(max-width: 767px)') ?? null
  onMedia()
  media?.addEventListener('change', onMedia)
})
onBeforeUnmount(() => media?.removeEventListener('change', onMedia))

const root = useTemplateRef('root')
const previewBox = useTemplateRef('previewBox')
/** Task shown in the preview panel. */
const previewId = ref<string | null>(null)
const previewCard = computed(() => props.cards?.find((c) => c.id === previewId.value) ?? null)
const previewPos = ref({ top: 0, left: 0 })
const previewWidth = 340

/** Opens the preview beside the task: to its right when it fits, else to its left. */
function openPreview(card: Card, event: MouseEvent | KeyboardEvent) {
  if (previewId.value === card.id) return closePreview(true)
  const chip = (event.currentTarget as HTMLElement).getBoundingClientRect()
  const box = root.value!.getBoundingClientRect()
  const gap = 10
  let left = chip.right - box.left + gap
  if (left + previewWidth > box.width) left = chip.left - box.left - previewWidth - gap
  previewPos.value = {
    top: Math.max(0, chip.top - box.top - 12),
    left: Math.max(0, Math.min(left, box.width - previewWidth)),
  }
  previewId.value = card.id
}

function closePreview(focusTask = false) {
  const id = previewId.value
  previewId.value = null
  if (focusTask && id)
    void nextTick(() => root.value?.querySelector<HTMLElement>(`[data-card-chip="${id}"]`)?.focus())
}

function openFromPreview() {
  const id = previewId.value
  closePreview()
  if (id) emit('open', id)
}

function closeOnOutside(event: MouseEvent) {
  const target = event.target as HTMLElement
  if (!previewId.value || previewBox.value?.contains(target)) return
  if (target.closest('[data-card-chip]')) return
  closePreview()
}
onMounted(() => document.addEventListener('mousedown', closeOnOutside))
onBeforeUnmount(() => document.removeEventListener('mousedown', closeOnOutside))

// The task left the calendar (filtered out, or moved out of range): drop its preview.
watch(previewCard, (card) => {
  if (!card && previewId.value && props.cards) previewId.value = null
})

const picked = ref<'month' | 'week'>('month')
const mode = computed(() => (narrow.value ? 'week' : picked.value))

const today = dayKey(new Date())
const shownMonth = computed(() => parseMonth(props.month))
/** First day to show in week mode: today in the current month, else the 1st of the shown month. */
const anchorFor = () =>
  monthKey(shownMonth.value) === monthKey(new Date()) ? new Date() : shownMonth.value
const weekAnchor = ref(anchorFor())

function pickMode(value: 'month' | 'week') {
  if (value === 'week') weekAnchor.value = anchorFor()
  picked.value = value
}

const days = computed(() =>
  mode.value === 'week' ? weekDays(weekAnchor.value) : monthGrid(shownMonth.value),
)
const byDate = computed(() => byDay(props.cards ?? []))
/** Month cells opened past the "+N more" fold. */
const expanded = ref(new Set<string>())

watch(
  days,
  (value, old) => {
    expanded.value = new Set()
    if (!old || dayKey(value[0]!) !== dayKey(old[0]!) || value.length !== old.length)
      emit('range', rangeOf(value))
  },
  { immediate: true },
)

const title = computed(() => {
  if (mode.value === 'month')
    return shownMonth.value.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
  const fmt = (d: Date) => d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  return `${fmt(days.value[0]!)} – ${fmt(days.value[6]!)}`
})

function setMonth(date: Date) {
  const key = monthKey(date)
  emit('update:month', key === monthKey(new Date()) ? null : key)
}

function step(delta: -1 | 1) {
  if (mode.value === 'week') {
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
  weekDays(new Date()).map((d) => ({
    long: d.toLocaleDateString(undefined, { weekday: 'long' }),
    short: d.toLocaleDateString(undefined, { weekday: 'short' }),
  })),
)

function shown(day: Date) {
  const all = byDate.value.get(dayKey(day)) ?? []
  if (mode.value === 'week' || expanded.value.has(dayKey(day))) return all
  return all.slice(0, perCell)
}

const hiddenCount = (day: Date) =>
  mode.value === 'week' || expanded.value.has(dayKey(day))
    ? 0
    : Math.max(0, (byDate.value.get(dayKey(day))?.length ?? 0) - perCell)

function expand(day: Date) {
  expanded.value = new Set([...expanded.value, dayKey(day)])
}

const dragging = ref<string | null>(null)
const over = ref<string | null>(null)

function onDragStart(event: DragEvent, card: Card) {
  if (!props.editable) return
  closePreview()
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

const tone = (card: Card) => cardTone(card)

const time = (card: Card) =>
  card.dueAt
    ? new Date(card.dueAt).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
    : ''

const listById = computed(() => new Map(props.lists.map((l) => [l.id, l])))

const dayLabel = (d: Date) =>
  d.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })
const outside = (d: Date) => mode.value === 'month' && d.getMonth() !== shownMonth.value.getMonth()

const segment = (active: boolean) =>
  active
    ? 'bg-panel font-semibold text-ink shadow-card'
    : 'font-medium text-ink-soft hover:text-ink'
</script>

<template>
  <div ref="root" class="relative flex flex-col gap-4">
    <div class="cal-head">
      <div
        v-if="!narrow"
        role="radiogroup"
        aria-label="Calendar range"
        class="flex gap-1 justify-self-start rounded-[12px] bg-hover p-1"
      >
        <button
          v-for="value in ['week', 'month'] as const"
          :key="value"
          type="button"
          role="radio"
          :aria-checked="mode === value"
          class="seg h-8 rounded-[9px] px-3.5 text-sm capitalize"
          :class="segment(mode === value)"
          @click="pickMode(value)"
        >
          {{ value }}
        </button>
      </div>

      <div class="flex items-center justify-center gap-1 justify-self-center">
        <button
          type="button"
          class="ghost flex h-9 w-9 items-center justify-center rounded-full text-lg text-muted"
          :aria-label="mode === 'week' ? 'Previous week' : 'Previous month'"
          @click="step(-1)"
        >
          ‹
        </button>
        <h2
          class="min-w-[11ch] text-center text-lg font-semibold tracking-tight"
          aria-live="polite"
        >
          {{ title }}
        </h2>
        <button
          type="button"
          class="ghost flex h-9 w-9 items-center justify-center rounded-full text-lg text-muted"
          :aria-label="mode === 'week' ? 'Next week' : 'Next month'"
          @click="step(1)"
        >
          ›
        </button>
      </div>

      <button
        type="button"
        class="ghost h-9 justify-self-end rounded-full border border-line px-4 text-sm font-semibold"
        @click="goToday"
      >
        Today
      </button>
    </div>

    <p v-if="undatedCount" class="-mt-1 text-sm text-muted">
      {{ undatedCount }} {{ undatedCount === 1 ? 'task has' : 'tasks have' }} no due date.
      <button type="button" class="font-semibold text-accent-strong" @click="emit('showUndated')">
        Show in List
      </button>
    </p>
    <p v-if="editable" class="sr-only">
      Drag a task to another day, or focus it and press Alt with an arrow key.
    </p>

    <div
      v-if="cards === null"
      class="cal-frame"
      :class="mode"
      aria-busy="true"
      aria-label="Loading calendar"
    >
      <div v-for="i in mode === 'week' ? 7 : 35" :key="i" class="bg-panel p-2.5">
        <SkeletonBlock height="14px" width="24px" />
      </div>
    </div>

    <div
      v-else
      class="cal-frame"
      :class="[mode, { stacked: narrow }]"
      role="grid"
      :aria-label="title"
    >
      <template v-if="!narrow">
        <div
          v-for="name in weekdayNames"
          :key="name.long"
          role="columnheader"
          class="bg-sunken py-2.5 text-center text-[13px] font-semibold text-ink-soft"
        >
          <span class="hidden lg:inline">{{ name.long }}</span>
          <span class="lg:hidden">{{ name.short }}</span>
        </div>
      </template>

      <div
        v-for="(day, i) in days"
        :key="dayKey(day)"
        role="gridcell"
        :aria-label="dayLabel(day)"
        :data-day="dayKey(day)"
        class="day fade-in flex min-w-0 flex-col gap-1.5 p-2"
        :class="[
          outside(day) ? 'bg-sunken' : 'bg-panel',
          { 'is-over': over === dayKey(day), 'is-today': dayKey(day) === today },
        ]"
        :style="{ '--i': Math.min(i, 12) }"
        @dragover.prevent="editable && (over = dayKey(day))"
        @dragleave="over === dayKey(day) && (over = null)"
        @drop.prevent="onDrop($event, dayKey(day))"
      >
        <span class="flex items-center gap-2 px-0.5">
          <span
            class="flex h-7 min-w-7 items-center justify-center rounded-full px-1 text-[15px] font-semibold"
            :class="
              dayKey(day) === today
                ? 'bg-accent text-on-accent'
                : outside(day)
                  ? 'text-muted'
                  : 'text-ink'
            "
            >{{ day.getDate() }}</span
          >
          <span v-if="narrow" class="text-xs font-semibold text-muted">{{
            day.toLocaleDateString(undefined, { weekday: 'long' })
          }}</span>
        </span>

        <button
          v-for="card in shown(day)"
          :key="card.id"
          type="button"
          :draggable="editable"
          :data-card-chip="card.id"
          class="task lift flex min-w-0 flex-col items-start gap-0.5 rounded-[8px] py-1.5 pr-2 pl-2.5 text-left"
          :class="{
            'is-dragging': dragging === card.id,
            'cursor-grab': editable,
            'is-done': !!card.completedAt,
          }"
          :style="{ '--tone': tone(card) }"
          :aria-label="`${card.key} ${card.title}${card.blocked ? ', blocked' : ''}`"
          :aria-keyshortcuts="
            editable ? 'Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown' : undefined
          "
          aria-haspopup="dialog"
          :aria-expanded="previewId === card.id"
          @click="openPreview(card, $event)"
          @keydown="onChipKey($event, card)"
          @dragstart="onDragStart($event, card)"
          @dragend="dragging = null"
        >
          <span
            v-if="card.labels[0] && mode === 'week'"
            class="text-[11px] font-semibold opacity-80"
            >{{ card.labels[0].name }}</span
          >
          <span
            class="w-full truncate text-xs font-semibold"
            :class="{ 'line-clamp-2 whitespace-normal': mode === 'week' }"
            ><span v-if="card.blocked" aria-hidden="true" data-testid="blocked-chip">🔒 </span
            ><span v-if="card.emoji" aria-hidden="true">{{ card.emoji }} </span
            >{{ card.title }}</span
          >
          <span v-if="mode === 'week'" class="text-[11px] opacity-70">{{ time(card) }}</span>
        </button>

        <button
          v-if="hiddenCount(day)"
          type="button"
          class="ghost self-start rounded-md px-1.5 py-0.5 text-xs font-semibold text-muted"
          :aria-label="`Show ${hiddenCount(day)} more tasks on ${dayLabel(day)}`"
          @click="expand(day)"
        >
          +{{ hiddenCount(day) }} more
        </button>
      </div>
    </div>
    <div
      v-if="previewCard"
      ref="previewBox"
      :class="narrow ? 'sheet-box fixed inset-x-4 bottom-4 z-40' : 'absolute z-40'"
      :style="narrow ? undefined : { top: `${previewPos.top}px`, left: `${previewPos.left}px` }"
    >
      <CalendarTaskPreview
        :card="previewCard"
        :list="listById.get(previewCard.listId)"
        :editable="editable"
        @close="closePreview(true)"
        @open="openFromPreview"
        @toggle-complete="emit('toggleComplete', previewCard.id)"
      />
    </div>
  </div>
</template>

<style scoped>
.cal-head {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 8px;
}

/* Hairline grid: the frame's background shows through 1px gaps between cells. */
.cal-frame {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 1px;
  overflow: hidden;
  border: 1px solid var(--color-line-soft);
  border-radius: 18px;
  background: var(--color-line-soft);
}

.cal-frame.month .day {
  min-height: 112px;
}

.cal-frame.week .day {
  min-height: 320px;
}

.cal-frame.stacked {
  grid-template-columns: 1fr;
}

.cal-frame.stacked .day {
  min-height: 64px;
}

.seg,
.day {
  transition:
    background-color var(--d-base) var(--ease-out),
    box-shadow var(--d-base) var(--ease-out),
    color var(--d-base) var(--ease-out);
}

.day.is-today {
  box-shadow: inset 0 2px 0 var(--color-accent);
}

.day.is-over {
  background: color-mix(in srgb, var(--color-accent) 7%, var(--color-panel));
  box-shadow: inset 0 0 0 2px var(--color-accent);
}

.task {
  border-left: 3px solid var(--tone);
  background: color-mix(in srgb, var(--tone) 10%, var(--color-panel));
  color: color-mix(in srgb, var(--tone) var(--tone-text-mix), var(--color-ink));
}

.task:hover {
  background: color-mix(in srgb, var(--tone) 16%, var(--color-panel));
}

.task.is-done {
  text-decoration: line-through;
}

.task.is-dragging {
  opacity: 0.45;
}

.sheet-box :deep(.preview) {
  width: 100%;
}

@media (max-width: 767px) {
  .cal-head {
    grid-template-columns: 1fr auto;
  }
}
</style>
