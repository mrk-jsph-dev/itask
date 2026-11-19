<script setup lang="ts">
import { computed, nextTick, useId, useTemplateRef } from 'vue'

import { useMenu } from '@/shared/ui/menu'
import type { Label, Member } from '../api'
import {
  activeCount,
  emptyFilter,
  groupKeys,
  groupLabels,
  sortKeys,
  sortLabels,
  views,
  type BoardState,
  type BoardViewKind,
  type GroupKey,
  type SortKey,
} from '../filters'
import FilterPopover from './FilterPopover.vue'

const props = defineProps<{
  state: BoardState
  labels: Label[]
  members: Pick<Member, 'userId' | 'displayName' | 'avatarColor'>[]
}>()
const emit = defineEmits<{ change: [change: Partial<BoardState>] }>()

const viewMeta: Record<BoardViewKind, { label: string; icon: string }> = {
  board: { label: 'Board', icon: 'M3 4h18v16H3zM9 4v16M15 4v16' },
  list: { label: 'List', icon: 'M9 6h11M9 12h11M9 18h11M4 6h.01M4 12h.01M4 18h.01' },
  calendar: { label: 'Calendar', icon: 'M3 5h18v16H3zM16 3v4M8 3v4M3 10h18' },
}
const icons = {
  filter: 'M3 5h18l-7 8v6l-4 2v-8z',
  sort: 'M7 4v16M3 16l4 4 4-4M17 20V4M13 8l4-4 4 4',
  group: 'M3 3h7v7H3zM14 3h7v7h-7zM3 14h18v7H3z',
}

const tabs = useTemplateRef<HTMLButtonElement[]>('tabs')
async function onTabKey(event: KeyboardEvent, index: number) {
  const delta = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
  if (!delta) return
  event.preventDefault()
  const next = views[(index + delta + views.length) % views.length]!
  emit('change', { view: next })
  await nextTick()
  tabs.value?.[views.indexOf(next)]?.focus()
}

const count = computed(() => activeCount(props.state.filter))

const filterId = useId()
const filterTrigger = useTemplateRef('filterTrigger')
const filterPanel = useTemplateRef<InstanceType<typeof FilterPopover>>('filterPanel')
const filterEl = computed(() => (filterPanel.value?.$el as HTMLElement | undefined) ?? null)
const {
  open: filterOpen,
  close: closeFilter,
  toggle: toggleFilter,
} = useMenu(filterEl, filterTrigger)

const sortId = useId()
const sortMenu = useTemplateRef('sortMenu')
const sortTrigger = useTemplateRef('sortTrigger')
const sort = useMenu(sortMenu, sortTrigger)

const groupId = useId()
const groupMenu = useTemplateRef('groupMenu')
const groupTrigger = useTemplateRef('groupTrigger')
const group = useMenu(groupMenu, groupTrigger)

function pickSort(key: SortKey) {
  sort.close(true)
  emit('change', { sort: key })
}

function pickGroup(key: GroupKey) {
  group.close(true)
  emit('change', { group: key })
}

function clear() {
  emit('change', { filter: emptyFilter() })
}

const toolClass =
  'ghost relative flex h-[38px] items-center gap-1.5 rounded-[10px] px-3 text-sm font-medium text-[#374151]'
const menuClass =
  'pop absolute top-11 right-0 z-30 flex w-48 flex-col rounded-[var(--radius-control)] border border-line-soft bg-panel p-1 text-sm shadow-[0_10px_24px_rgb(17_24_39/0.12)]'
</script>

<template>
  <div class="flex flex-wrap items-center gap-3 border-b border-[#EEF1F6] px-5 py-3.5 md:px-7">
    <div role="tablist" aria-label="Board view" class="flex gap-1 rounded-[14px] bg-hover p-1">
      <button
        v-for="(view, i) in views"
        :key="view"
        ref="tabs"
        type="button"
        role="tab"
        :aria-selected="state.view === view"
        :tabindex="state.view === view ? 0 : -1"
        class="tab flex h-[38px] items-center gap-2 rounded-[10px] px-4 text-[15px]"
        :class="
          state.view === view
            ? 'bg-panel font-semibold text-ink shadow-[0_1px_2px_rgb(17_24_39/0.08)]'
            : 'font-medium text-[#374151] hover:text-ink'
        "
        @click="emit('change', { view })"
        @keydown="onTabKey($event, i)"
      >
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="viewMeta[view].icon" />
        </svg>
        {{ viewMeta[view].label }}
      </button>
    </div>

    <div class="ml-auto flex flex-wrap items-center gap-1">
      <button
        v-if="count"
        type="button"
        class="ghost h-[38px] rounded-[10px] px-3 text-sm font-semibold text-accent"
        @click="clear"
      >
        Clear filters
      </button>

      <div class="relative">
        <button
          ref="filterTrigger"
          type="button"
          :class="[toolClass, count ? 'bg-[#EFF4FF] text-accent' : '']"
          aria-haspopup="dialog"
          :aria-expanded="filterOpen"
          :aria-controls="filterId"
          :aria-label="count ? `Filter, ${count} active` : 'Filter'"
          @click.stop="toggleFilter"
        >
          <svg
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path :d="icons.filter" />
          </svg>
          Filter
          <span
            v-if="count"
            class="rounded-full bg-accent px-1.5 text-[11px] leading-[18px] font-semibold text-white"
            aria-hidden="true"
            >{{ count }}</span
          >
        </button>
        <FilterPopover
          v-if="filterOpen"
          :id="filterId"
          ref="filterPanel"
          :filter="state.filter"
          :labels="labels"
          :members="members"
          @change="(filter) => emit('change', { filter })"
          @close="closeFilter(true)"
        />
      </div>

      <div class="relative">
        <button
          ref="sortTrigger"
          type="button"
          :class="[toolClass, state.sort !== 'position' ? 'text-accent' : '']"
          aria-haspopup="menu"
          :aria-expanded="sort.open.value"
          :aria-controls="sortId"
          @click.stop="sort.toggle"
        >
          <svg
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path :d="icons.sort" />
          </svg>
          {{ state.sort === 'position' ? 'Sort' : `Sort: ${sortLabels[state.sort]}` }}
        </button>
        <div
          v-if="sort.open.value"
          :id="sortId"
          ref="sortMenu"
          role="menu"
          aria-label="Sort by"
          :class="menuClass"
          @keydown="sort.onKeydown"
        >
          <button
            v-for="key in sortKeys"
            :key="key"
            type="button"
            role="menuitemradio"
            :aria-checked="state.sort === key"
            class="ghost flex items-center justify-between rounded-lg px-3 py-2 text-left"
            @click="pickSort(key)"
          >
            {{ sortLabels[key] }}
            <span v-if="state.sort === key" class="text-accent" aria-hidden="true">✓</span>
          </button>
        </div>
      </div>

      <div v-if="state.view === 'list'" class="relative">
        <button
          ref="groupTrigger"
          type="button"
          :class="[toolClass, state.group !== 'none' ? 'text-accent' : '']"
          aria-haspopup="menu"
          :aria-expanded="group.open.value"
          :aria-controls="groupId"
          @click.stop="group.toggle"
        >
          <svg
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path :d="icons.group" />
          </svg>
          {{ state.group === 'none' ? 'Group by' : `Group: ${groupLabels[state.group]}` }}
        </button>
        <div
          v-if="group.open.value"
          :id="groupId"
          ref="groupMenu"
          role="menu"
          aria-label="Group by"
          :class="menuClass"
          @keydown="group.onKeydown"
        >
          <button
            v-for="key in groupKeys"
            :key="key"
            type="button"
            role="menuitemradio"
            :aria-checked="state.group === key"
            class="ghost flex items-center justify-between rounded-lg px-3 py-2 text-left"
            @click="pickGroup(key)"
          >
            {{ groupLabels[key] }}
            <span v-if="state.group === key" class="text-accent" aria-hidden="true">✓</span>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tab {
  transition:
    background-color var(--d-base) var(--ease-out),
    color var(--d-base) var(--ease-out);
}
</style>
