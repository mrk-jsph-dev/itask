<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

import { labelIndexes, niceScale, type BarGroup } from '../chart'

/** Done vs created per day (or week) as paired bars, with a hover tooltip and a table for screen readers. */
const props = defineProps<{ groups: BarGroup[]; unit: 'day' | 'week' }>()

const root = ref<HTMLElement | null>(null)
const width = ref(600)
const height = 200
const pad = { top: 12, right: 8, bottom: 26, left: 32 }
const hovered = ref<number | null>(null)
let observer: ResizeObserver | undefined

onMounted(() => {
  if (!root.value) return
  width.value = root.value.clientWidth || 600
  if (typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver(([entry]) => {
    if (entry) width.value = Math.max(240, entry.contentRect.width)
  })
  observer.observe(root.value)
})
onBeforeUnmount(() => observer?.disconnect())

const scale = computed(() =>
  niceScale(Math.max(0, ...props.groups.flatMap((g) => [g.done, g.created]))),
)
const plotWidth = computed(() => width.value - pad.left - pad.right)
const plotHeight = height - pad.top - pad.bottom
const slot = computed(() => plotWidth.value / Math.max(1, props.groups.length))
/** Two bars per slot with a 2px gap between them and room between slots. */
const bar = computed(() => Math.max(2, Math.min(18, (slot.value * 0.7 - 2) / 2)))
const y = (value: number) => pad.top + plotHeight - (value / scale.value.max) * plotHeight
const x = (index: number) => pad.left + index * slot.value + slot.value / 2

/** A bar with 4px rounded top corners anchored on the baseline (zero draws nothing). */
function barPath(left: number, value: number) {
  if (value <= 0) return ''
  const top = y(value)
  const bottom = y(0)
  const w = bar.value
  const r = Math.min(4, w / 2, bottom - top)
  return `M${left},${bottom}V${top + r}Q${left},${top} ${left + r},${top}H${left + w - r}Q${left + w},${top} ${left + w},${top + r}V${bottom}Z`
}

/** About one axis label per 110px so dates never collide on a phone. */
const labels = computed(
  () => new Set(labelIndexes(props.groups.length, Math.max(2, Math.floor(plotWidth.value / 110)))),
)
const tip = computed(() => (hovered.value === null ? null : props.groups[hovered.value]))
const tipLeft = computed(() =>
  hovered.value === null ? 0 : Math.min(Math.max(x(hovered.value), 80), width.value - 80),
)
</script>

<template>
  <div ref="root" class="relative w-full" @mouseleave="hovered = null">
    <div class="mb-2 flex flex-wrap items-center gap-4 text-[13px] text-muted" aria-hidden="true">
      <span class="flex items-center gap-1.5"
        ><span class="h-2.5 w-2.5 rounded-[3px] bg-chart-done" />Done</span
      >
      <span class="flex items-center gap-1.5"
        ><span class="h-2.5 w-2.5 rounded-[3px] bg-chart-created" />Created</span
      >
      <span class="ml-auto">per {{ unit }}</span>
    </div>
    <svg
      :width="width"
      :height="height"
      :viewBox="`0 0 ${width} ${height}`"
      class="block max-w-full"
      role="img"
      :aria-label="`Cards done and created per ${unit}`"
      data-testid="throughput-chart"
    >
      <g>
        <template v-for="t in scale.ticks" :key="t">
          <line
            :x1="pad.left"
            :x2="width - pad.right"
            :y1="y(t)"
            :y2="y(t)"
            :stroke="t === 0 ? 'var(--color-line)' : 'var(--color-line-soft)'"
            stroke-width="1"
          />
          <text
            :x="pad.left - 8"
            :y="y(t)"
            text-anchor="end"
            dominant-baseline="middle"
            class="fill-muted text-[11px] tabular-nums"
          >
            {{ t }}
          </text>
        </template>
      </g>
      <g v-for="(g, i) in groups" :key="g.from">
        <rect
          v-if="hovered === i"
          :x="pad.left + i * slot"
          :y="pad.top"
          :width="slot"
          :height="plotHeight"
          fill="var(--color-hover)"
        />
        <path :d="barPath(x(i) - bar - 1, g.done)" fill="var(--color-chart-done)" />
        <path :d="barPath(x(i) + 1, g.created)" fill="var(--color-chart-created)" />
        <text
          v-if="labels.has(i)"
          :x="x(i)"
          :y="height - 8"
          :text-anchor="i === 0 ? 'start' : i === groups.length - 1 ? 'end' : 'middle'"
          class="fill-muted text-[11px]"
        >
          {{ g.label }}
        </text>
        <rect
          :x="pad.left + i * slot"
          :y="0"
          :width="slot"
          :height="height"
          fill="transparent"
          @mouseenter="hovered = i"
        />
      </g>
    </svg>
    <div
      v-if="tip"
      class="pointer-events-none absolute top-6 z-10 -translate-x-1/2 rounded-[10px] border border-line bg-panel px-3 py-2 text-[13px] shadow-[0_8px_24px_rgb(17_24_39/0.12)]"
      :style="{ left: `${tipLeft}px` }"
      role="presentation"
    >
      <p class="font-semibold text-ink">{{ tip.title }}</p>
      <p class="flex items-center gap-1.5 text-ink">
        <span class="h-2 w-2 rounded-[2px] bg-chart-done" aria-hidden="true" />Done
        <span class="ml-auto pl-3 font-semibold tabular-nums">{{ tip.done }}</span>
      </p>
      <p class="flex items-center gap-1.5 text-ink">
        <span class="h-2 w-2 rounded-[2px] bg-chart-created" aria-hidden="true" />Created
        <span class="ml-auto pl-3 font-semibold tabular-nums">{{ tip.created }}</span>
      </p>
    </div>
    <table class="sr-only">
      <caption>
        Cards done and created per
        {{
          unit
        }}
      </caption>
      <thead>
        <tr>
          <th scope="col">{{ unit === 'day' ? 'Day' : 'Week' }}</th>
          <th scope="col">Done</th>
          <th scope="col">Created</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="g in groups" :key="g.from">
          <th scope="row">{{ g.title }}</th>
          <td>{{ g.done }}</td>
          <td>{{ g.created }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
