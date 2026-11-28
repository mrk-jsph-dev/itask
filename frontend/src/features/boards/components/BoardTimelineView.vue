<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import type { BoardList, Card, CardPatch, DependencyLink, Label, Member } from '../api'
import { dayKey } from '../calendar'
import { cardTone } from '../cardOptions'
import { groupCards, zooms, type CardRange, type GroupKey, type TimelineZoom } from '../filters'
import {
  barGeometry,
  dayIndex,
  dragDays,
  fromKeyFor,
  rangeText,
  resizeDates,
  shiftDates,
  stepWindow,
  windowFor,
  windowStart,
  withPatch,
  type BarGeometry,
} from '../timeline'

const props = defineProps<{
  /** Cards whose dates overlap the shown window; null while loading. */
  cards: Card[] | null
  /** Matching cards with neither a start nor a due date. */
  undated: Card[]
  /** Blocker links between `cards`. */
  dependencies: DependencyLink[]
  zoom: TimelineZoom
  /** Window start as `yyyy-mm-dd`; null = the window holding today. */
  from: string | null
  group: GroupKey
  editable: boolean
  lists: Pick<BoardList, 'id' | 'name' | 'emoji'>[]
  labels: Label[]
  members: Pick<Member, 'userId' | 'displayName'>[]
}>()
const emit = defineEmits<{
  window: [change: { zoom?: TimelineZoom; from: string | null }]
  range: [range: CardRange]
  open: [cardId: string]
  update: [cardId: string, patch: CardPatch]
}>()

const zoomLabels: Record<TimelineZoom, string> = {
  week: 'Week',
  month: 'Month',
  quarter: 'Quarter',
}
const groupHeight = 34
const rowHeight = 40

/** Phones get a stacked agenda without drag. */
const narrow = ref(false)
let media: MediaQueryList | null = null
const onMedia = () => (narrow.value = !!media?.matches)

const arrowsKey = 'itask.timeline.arrows'
function storedArrows(): boolean {
  try {
    return localStorage.getItem(arrowsKey) !== 'off'
  } catch {
    return true
  }
}
const arrows = ref(storedArrows())
function toggleArrows() {
  arrows.value = !arrows.value
  try {
    localStorage.setItem(arrowsKey, arrows.value ? 'on' : 'off')
  } catch {
    // Storage blocked: the toggle still works for this visit.
  }
}

const track = useTemplateRef('track')
const trackWidth = ref(0)
let observer: ResizeObserver | null = null
onMounted(() => {
  media = window.matchMedia?.('(max-width: 767px)') ?? null
  onMedia()
  media?.addEventListener('change', onMedia)
})
watch(track, (el) => {
  observer?.disconnect()
  if (!el) return
  trackWidth.value = el.getBoundingClientRect().width
  if (typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver(([entry]) => (trackWidth.value = entry?.contentRect.width ?? 0))
  observer.observe(el)
})
onBeforeUnmount(() => {
  media?.removeEventListener('change', onMedia)
  observer?.disconnect()
})

const win = computed(() => windowFor(props.from, props.zoom))
const n = computed(() => win.value.days.length)

watch(
  () => `${dayKey(win.value.from)}/${dayKey(win.value.to)}`,
  () => emit('range', { from: win.value.from, to: win.value.to, kind: 'window' }),
  { immediate: true },
)

const today = new Date()
const todayIndex = computed(() => dayIndex(today, win.value.from))
const todayShown = computed(() => todayIndex.value >= 0 && todayIndex.value < n.value)

const fmt = (d: Date, opts: Intl.DateTimeFormatOptions) => d.toLocaleDateString(undefined, opts)
const title = computed(() => {
  if (props.zoom === 'month') return fmt(win.value.from, { month: 'long', year: 'numeric' })
  const last = win.value.days[n.value - 1]!
  return `${fmt(win.value.from, { month: 'short', day: 'numeric' })} – ${fmt(last, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })}`
})

function step(delta: -1 | 1) {
  emit('window', { from: fromKeyFor(stepWindow(win.value.from, props.zoom, delta), props.zoom) })
}

function pickZoom(zoom: TimelineZoom) {
  if (zoom === props.zoom) return
  const anchor = todayShown.value ? today : win.value.from
  emit('window', { zoom, from: fromKeyFor(windowStart(anchor, zoom), zoom) })
}

/** Axis labels: every day for week and month, Mondays for quarter. */
const ticks = computed(() =>
  win.value.days
    .map((day, i) => ({ day, i }))
    .filter(({ day }) => props.zoom !== 'quarter' || day.getDay() === 1)
    .map(({ day, i }) => ({
      key: dayKey(day),
      left: (i / n.value) * 100,
      width: props.zoom === 'quarter' ? (7 / n.value) * 100 : (1 / n.value) * 100,
      text:
        props.zoom === 'quarter'
          ? fmt(day, { month: 'short', day: 'numeric' })
          : String(day.getDate()),
      sub: props.zoom === 'week' ? fmt(day, { weekday: 'narrow' }) : '',
      today: dayKey(day) === dayKey(today),
    })),
)

const weekends = computed(() =>
  win.value.days
    .map((day, i) => ({ day, i }))
    .filter(({ day }) => day.getDay() === 0 || day.getDay() === 6)
    .map(({ day, i }) => ({ key: dayKey(day), left: (i / n.value) * 100, width: 100 / n.value })),
)

const groups = computed(() =>
  groupCards(props.cards ?? [], props.group === 'none' ? 'list' : props.group, {
    lists: props.lists,
    labels: props.labels,
    members: props.members,
  }),
)

/** A drag in progress: which card, which part, and the day delta so far. */
interface Drag {
  cardId: string
  mode: 'move' | 'start' | 'end'
  startX: number
  width: number
  delta: number
  moved: boolean
}
const drag = ref<Drag | null>(null)
let suppressClick = false

function patchFor(card: Card, mode: Drag['mode'], days: number): CardPatch | null {
  return mode === 'move' ? shiftDates(card, days) : resizeDates(card, mode, days)
}

/** The card as drawn: with the drag applied while one is in progress. */
function drawn(card: Card): Card {
  const d = drag.value
  if (!d || d.cardId !== card.id || !d.delta) return card
  return withPatch(card, patchFor(card, d.mode, d.delta))
}

const geometry = (card: Card): BarGeometry | null => barGeometry(drawn(card), win.value)

/** Each card's first row (y = row middle) and geometry, for the arrows. */
const rowsById = computed(() => {
  const rows = new Map<string, { y: number; g: BarGeometry }>()
  let y = 0
  for (const group of groups.value) {
    y += groupHeight
    for (const card of group.cards) {
      const g = geometry(card)
      if (g && !rows.has(card.id)) rows.set(card.id, { y: y + rowHeight / 2, g })
      y += rowHeight
    }
  }
  return rows
})
const bodyHeight = computed(() =>
  groups.value.reduce((h, g) => h + groupHeight + g.cards.length * rowHeight, 0),
)

const links = computed(() => {
  const width = trackWidth.value
  if (!arrows.value || !width) return []
  return props.dependencies.flatMap((link) => {
    const from = rowsById.value.get(link.blockedById)
    const to = rowsById.value.get(link.cardId)
    if (!from || !to) return []
    const x1 = (width * (from.g.left + from.g.width)) / 100
    const x2 = (width * to.g.left) / 100
    const bend = Math.max(16, Math.abs(x2 - x1) / 2)
    return [
      {
        key: `${link.cardId}-${link.blockedById}`,
        d: `M ${x1} ${from.y} C ${x1 + bend} ${from.y}, ${x2 - bend} ${to.y}, ${x2 - 4} ${to.y}`,
        late: to.g.startDay < from.g.endDay,
      },
    ]
  })
})

function onPointerDown(event: PointerEvent, card: Card, mode: Drag['mode']) {
  if (!props.editable || event.button !== 0) return
  event.stopPropagation()
  const width = track.value?.getBoundingClientRect().width ?? trackWidth.value
  drag.value = { cardId: card.id, mode, startX: event.clientX, width, delta: 0, moved: false }
  ;(event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId)
}

function onPointerMove(event: PointerEvent) {
  const d = drag.value
  if (!d) return
  const dx = event.clientX - d.startX
  if (Math.abs(dx) > 3) d.moved = true
  d.delta = dragDays(dx, d.width, n.value)
}

function onPointerUp(card: Card) {
  const d = drag.value
  drag.value = null
  if (!d || d.cardId !== card.id) return
  if (d.moved) suppressClick = true
  const patch = d.delta ? patchFor(card, d.mode, d.delta) : null
  if (patch) emit('update', card.id, patch)
}

function onBarClick(card: Card) {
  if (suppressClick) {
    suppressClick = false
    return
  }
  emit('open', card.id)
}

/** Alt+←/→ moves a day; Alt+Shift+←/→ moves the due date (or the focused handle's end). */
function onKey(event: KeyboardEvent, card: Card, edge?: 'start' | 'end') {
  if (!props.editable || !event.altKey) return
  const delta = { ArrowLeft: -1, ArrowRight: 1 }[event.key]
  if (!delta) return
  event.preventDefault()
  const patch = edge
    ? resizeDates(card, edge, delta)
    : event.shiftKey
      ? resizeDates(card, 'end', delta)
      : shiftDates(card, delta)
  if (patch) emit('update', card.id, patch)
}

const label = (card: Card) =>
  `${card.key} ${card.title}, ${rangeText(card)}${card.blocked ? ', blocked' : ''}`

const segment = (active: boolean) =>
  active
    ? 'bg-panel font-semibold text-ink shadow-card'
    : 'font-medium text-ink-soft hover:text-ink'

const pct = (value: number) => `${value}%`
</script>

<template>
  <div class="flex flex-col gap-4" data-testid="timeline">
    <div class="tl-head">
      <div
        role="radiogroup"
        aria-label="Timeline zoom"
        class="flex gap-1 justify-self-start rounded-[12px] bg-hover p-1"
      >
        <button
          v-for="value in zooms"
          :key="value"
          type="button"
          role="radio"
          :aria-checked="zoom === value"
          class="seg h-8 rounded-[9px] px-3 text-sm"
          :class="segment(zoom === value)"
          @click="pickZoom(value)"
        >
          {{ zoomLabels[value] }}
        </button>
      </div>

      <div class="tl-nav flex items-center justify-center gap-1 justify-self-center">
        <button
          type="button"
          class="ghost flex h-9 w-9 items-center justify-center rounded-full text-lg text-muted"
          aria-label="Earlier"
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
          aria-label="Later"
          @click="step(1)"
        >
          ›
        </button>
      </div>

      <div class="flex items-center gap-2 justify-self-end">
        <button
          v-if="!narrow"
          type="button"
          role="switch"
          :aria-checked="arrows"
          class="ghost h-9 rounded-full border border-line px-3.5 text-sm font-semibold"
          :class="arrows ? 'text-accent-strong' : 'text-ink-soft'"
          @click="toggleArrows"
        >
          ↳ Blockers
        </button>
        <button
          type="button"
          class="ghost h-9 rounded-full border border-line px-4 text-sm font-semibold"
          @click="emit('window', { from: null })"
        >
          Today
        </button>
      </div>
    </div>

    <p v-if="editable && !narrow" class="sr-only">
      Drag a bar to move its dates or drag an end to change it. With a bar focused, Alt with the
      left or right arrow moves it a day, and Alt with Shift changes the due date.
    </p>

    <div v-if="cards === null" class="tl-frame p-4" aria-busy="true" aria-label="Loading timeline">
      <div v-for="i in 5" :key="i" class="flex items-center gap-4 py-2">
        <SkeletonBlock height="14px" width="120px" />
        <SkeletonBlock height="22px" :width="`${20 + i * 9}%`" />
      </div>
    </div>

    <!-- Phones: one row per card with a short bar inside the window. -->
    <div v-else-if="narrow" class="flex flex-col gap-4" data-testid="timeline-agenda">
      <p v-if="!groups.length" class="text-sm text-muted">No tasks with dates in this range.</p>
      <section v-for="group in groups" :key="group.key" class="flex flex-col gap-1.5">
        <h3 class="flex items-center gap-1.5 text-sm font-semibold text-ink-soft">
          <span v-if="group.emoji" aria-hidden="true">{{ group.emoji }}</span>
          {{ group.title }}
          <span class="text-muted">{{ group.cards.length }}</span>
        </h3>
        <button
          v-for="card in group.cards"
          :key="card.id"
          type="button"
          class="agenda lift flex flex-col gap-1.5 rounded-[12px] border border-line-soft bg-panel px-3 py-2.5 text-left"
          :class="{ 'is-done': !!card.completedAt }"
          :style="{ '--tone': cardTone(card) }"
          :aria-label="label(card)"
          @click="emit('open', card.id)"
        >
          <span class="flex w-full items-center gap-2">
            <span class="min-w-0 flex-1 truncate text-sm font-semibold">
              <span v-if="card.blocked" aria-hidden="true">🔒 </span>
              <span v-if="card.emoji" aria-hidden="true">{{ card.emoji }} </span>{{ card.title }}
            </span>
            <span class="shrink-0 text-xs text-muted">{{ rangeText(card) }}</span>
          </span>
          <span class="relative block h-1.5 w-full rounded-full bg-sunken" aria-hidden="true">
            <span
              v-if="geometry(card)"
              class="mini tone-bg absolute top-0 h-1.5 rounded-full"
              :style="{
                left: pct(geometry(card)!.left),
                width: geometry(card)!.kind === 'dot' ? '6px' : pct(geometry(card)!.width),
              }"
            />
          </span>
        </button>
      </section>
    </div>

    <div v-else class="tl-frame">
      <div class="tl-row tl-axis">
        <div class="px-3 text-xs font-semibold text-muted">Task</div>
        <div ref="track" class="relative h-10">
          <span
            v-for="tick in ticks"
            :key="tick.key"
            class="absolute top-0 flex h-full flex-col items-center justify-center text-[11px] leading-tight"
            :class="tick.today ? 'font-bold text-accent-strong' : 'text-muted'"
            :style="{ left: pct(tick.left), width: pct(tick.width) }"
          >
            <span v-if="tick.sub">{{ tick.sub }}</span>
            <span :class="{ 'truncate px-1': zoom === 'quarter' }">{{ tick.text }}</span>
          </span>
        </div>
      </div>

      <div class="relative">
        <div class="tl-overlay" aria-hidden="true">
          <span
            v-for="w in weekends"
            :key="w.key"
            class="absolute inset-y-0 bg-sunken"
            :style="{ left: pct(w.left), width: pct(w.width) }"
          />
          <span
            v-if="todayShown"
            class="today absolute inset-y-0 w-0.5 bg-accent"
            data-testid="today-line"
            :style="{ left: pct(((todayIndex + 0.5) / n) * 100) }"
          />
        </div>

        <p v-if="!groups.length" class="relative px-4 py-8 text-center text-sm text-muted">
          No tasks with dates in this range.
        </p>

        <section v-for="group in groups" :key="group.key" class="relative">
          <h3
            class="tl-group flex items-center gap-1.5 px-3 text-[13px] font-semibold text-ink-soft"
            :style="{ height: `${groupHeight}px` }"
          >
            <span
              v-if="group.color"
              class="tone-bg h-2.5 w-2.5 rounded-full"
              :style="{ '--tone': group.color }"
              aria-hidden="true"
            />
            <span v-if="group.emoji" aria-hidden="true">{{ group.emoji }}</span>
            <span class="truncate">{{ group.title }}</span>
            <span class="font-medium text-muted">{{ group.cards.length }}</span>
          </h3>
          <div
            v-for="card in group.cards"
            :key="card.id"
            class="tl-row"
            :style="{ height: `${rowHeight}px` }"
          >
            <div
              class="min-w-0 truncate px-3 text-sm"
              :class="{ 'text-muted line-through': !!card.completedAt }"
            >
              <span v-if="card.blocked" aria-hidden="true">🔒 </span>
              <span v-if="card.emoji" aria-hidden="true">{{ card.emoji }} </span>{{ card.title }}
            </div>
            <div class="relative h-full">
              <template v-if="geometry(card)">
                <div
                  v-if="geometry(card)!.kind === 'bar'"
                  class="absolute top-1/2 h-7 -translate-y-1/2"
                  :style="{ left: pct(geometry(card)!.left), width: pct(geometry(card)!.width) }"
                >
                  <button
                    type="button"
                    class="bar flex h-full w-full items-center truncate rounded-[7px] pr-2 pl-2 text-left text-xs font-semibold"
                    :class="{
                      'is-done': !!card.completedAt,
                      'is-dragging': drag?.cardId === card.id,
                      'cut-start': geometry(card)!.clippedStart,
                      'cut-end': geometry(card)!.clippedEnd,
                      'cursor-grab': editable,
                    }"
                    :style="{ '--tone': cardTone(card) }"
                    :data-bar="card.id"
                    :title="rangeText(drawn(card))"
                    :aria-label="label(card)"
                    :aria-keyshortcuts="
                      editable
                        ? 'Alt+ArrowLeft Alt+ArrowRight Alt+Shift+ArrowLeft Alt+Shift+ArrowRight'
                        : undefined
                    "
                    @pointerdown="onPointerDown($event, card, 'move')"
                    @pointermove="onPointerMove"
                    @pointerup="onPointerUp(card)"
                    @pointercancel="drag = null"
                    @click="onBarClick(card)"
                    @keydown="onKey($event, card)"
                  >
                    <span class="truncate">{{ card.title }}</span>
                  </button>
                  <template v-if="editable">
                    <button
                      v-if="!geometry(card)!.clippedStart"
                      type="button"
                      class="handle left-0"
                      :data-handle="`start-${card.id}`"
                      :aria-label="`Change start of ${card.key}`"
                      aria-keyshortcuts="Alt+ArrowLeft Alt+ArrowRight"
                      @pointerdown="onPointerDown($event, card, 'start')"
                      @pointermove="onPointerMove"
                      @pointerup="onPointerUp(card)"
                      @pointercancel="drag = null"
                      @click.stop
                      @keydown="onKey($event, card, 'start')"
                    />
                    <button
                      v-if="!geometry(card)!.clippedEnd"
                      type="button"
                      class="handle right-0"
                      :data-handle="`end-${card.id}`"
                      :aria-label="`Change due date of ${card.key}`"
                      aria-keyshortcuts="Alt+ArrowLeft Alt+ArrowRight"
                      @pointerdown="onPointerDown($event, card, 'end')"
                      @pointermove="onPointerMove"
                      @pointerup="onPointerUp(card)"
                      @pointercancel="drag = null"
                      @click.stop
                      @keydown="onKey($event, card, 'end')"
                    />
                  </template>
                </div>
                <button
                  v-else
                  type="button"
                  class="dot tone-bg absolute top-1/2 h-3.5 w-3.5 -translate-x-1/2 -translate-y-1/2 rounded-full"
                  :class="{ 'is-dragging': drag?.cardId === card.id, 'cursor-grab': editable }"
                  :style="{ left: pct(geometry(card)!.left), '--tone': cardTone(card) }"
                  :data-bar="card.id"
                  :title="rangeText(drawn(card))"
                  :aria-label="label(card)"
                  @pointerdown="onPointerDown($event, card, 'move')"
                  @pointermove="onPointerMove"
                  @pointerup="onPointerUp(card)"
                  @pointercancel="drag = null"
                  @click="onBarClick(card)"
                  @keydown="onKey($event, card)"
                />
              </template>
            </div>
          </div>
        </section>

        <svg
          v-if="links.length"
          class="tl-arrows pointer-events-none"
          :height="bodyHeight"
          aria-hidden="true"
          data-testid="timeline-arrows"
        >
          <defs>
            <marker
              id="tl-arrow"
              viewBox="0 0 8 8"
              refX="4"
              refY="4"
              markerWidth="7"
              markerHeight="7"
              orient="auto"
            >
              <path d="M0 0 L8 4 L0 8 z" class="arrow-head" />
            </marker>
            <marker
              id="tl-arrow-late"
              viewBox="0 0 8 8"
              refX="4"
              refY="4"
              markerWidth="7"
              markerHeight="7"
              orient="auto"
            >
              <path d="M0 0 L8 4 L0 8 z" class="arrow-head late" />
            </marker>
          </defs>
          <path
            v-for="link in links"
            :key="link.key"
            :d="link.d"
            class="arrow"
            :class="{ late: link.late }"
            :marker-end="link.late ? 'url(#tl-arrow-late)' : 'url(#tl-arrow)'"
          />
        </svg>
      </div>
    </div>

    <details v-if="undated.length" class="rounded-[14px] border border-line-soft bg-panel">
      <summary class="cursor-pointer px-4 py-3 text-sm font-semibold text-ink-soft">
        No dates · {{ undated.length }} {{ undated.length === 1 ? 'task' : 'tasks' }}
      </summary>
      <ul class="flex flex-wrap gap-2 px-4 pb-4" data-testid="no-dates">
        <li v-for="card in undated" :key="card.id">
          <button
            type="button"
            class="ghost rounded-full border border-line px-3 py-1 text-sm"
            :aria-label="`${card.key} ${card.title}`"
            @click="emit('open', card.id)"
          >
            <span v-if="card.emoji" aria-hidden="true">{{ card.emoji }} </span>{{ card.title }}
          </button>
        </li>
      </ul>
    </details>
  </div>
</template>

<style scoped>
.tl-head {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 8px;
}

.tl-frame {
  --label-w: clamp(130px, 22%, 220px);
  overflow: hidden;
  border: 1px solid var(--color-line-soft);
  border-radius: 18px;
  background: var(--color-panel);
}

.tl-row {
  display: grid;
  grid-template-columns: var(--label-w) minmax(0, 1fr);
  align-items: center;
}

.tl-axis {
  border-bottom: 1px solid var(--color-line-soft);
  background: var(--color-sunken);
}

.tl-group {
  position: relative;
  border-top: 1px solid var(--color-line-faint);
}

.tl-overlay,
.tl-arrows {
  position: absolute;
  top: 0;
  bottom: 0;
  left: var(--label-w);
  right: 0;
}

.tl-arrows {
  width: calc(100% - var(--label-w));
  overflow: visible;
}

.today {
  opacity: 0.6;
}

.bar,
.dot {
  touch-action: none;
  transition:
    background-color var(--d-base) var(--ease-out),
    opacity var(--d-base) var(--ease-out);
}

.bar {
  border-left: 3px solid var(--tone);
  background: color-mix(in srgb, var(--tone) 14%, var(--color-panel));
  color: color-mix(in srgb, var(--tone) var(--tone-text-mix), var(--color-ink));
  min-width: 10px;
}

.bar:hover {
  background: color-mix(in srgb, var(--tone) 20%, var(--color-panel));
}

.bar.cut-start {
  border-left-style: dashed;
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
}

.bar.cut-end {
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
}

.bar.is-done {
  text-decoration: line-through;
}

.dot {
  box-shadow: 0 0 0 3px var(--color-panel);
}

.is-dragging {
  cursor: grabbing;
  opacity: 0.85;
}

.handle {
  position: absolute;
  top: 0;
  bottom: 0;
  width: 8px;
  cursor: ew-resize;
  touch-action: none;
  border-radius: 4px;
}

.handle:hover,
.handle:focus-visible {
  background: color-mix(in srgb, var(--color-accent) 35%, transparent);
}

.arrow {
  fill: none;
  stroke: var(--color-line-strong);
  stroke-width: 1.5;
}

.arrow.late {
  stroke: var(--color-danger);
}

.arrow-head {
  fill: var(--color-line-strong);
}

.arrow-head.late {
  fill: var(--color-danger);
}

.agenda {
  border-left: 3px solid var(--tone);
}

.agenda.is-done {
  text-decoration: line-through;
  color: var(--color-muted);
}

.mini {
  min-width: 6px;
}

@media (max-width: 767px) {
  .tl-head {
    grid-template-columns: 1fr auto;
  }

  .tl-nav {
    grid-column: 1 / -1;
    order: 3;
  }
}
</style>
