<script setup lang="ts" generic="T extends PaletteItem">
import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  useId,
  useTemplateRef,
  watch,
} from 'vue'

import type { PaletteItem, PaletteSection } from './commandPalette'
import SkeletonBlock from './SkeletonBlock.vue'

const props = defineProps<{
  sections: PaletteSection<T>[]
  label: string
  placeholder?: string
  loading?: boolean
  /** Shown when there are no rows and nothing is loading. */
  emptyText?: string
}>()
const emit = defineEmits<{ select: [item: T]; close: [] }>()
const query = defineModel<string>('query', { required: true })

const id = useId()
const input = useTemplateRef('input')
const listbox = useTemplateRef('listbox')
const active = ref(0)
let returnFocus: HTMLElement | null = null

/** Rows in display order, each with its index across sections. */
const rows = computed(() => {
  let index = 0
  return props.sections.map((section) => ({
    title: section.title,
    items: section.items.map((item) => ({ item, index: index++ })),
  }))
})
const flat = computed(() => props.sections.flatMap((s) => s.items))
const optionId = (index: number) => `${id}-option-${index}`

watch(flat, () => (active.value = 0))

async function move(step: number) {
  const count = flat.value.length
  if (!count) return
  active.value = (active.value + step + count) % count
  await nextTick()
  listbox.value
    ?.querySelector(`#${CSS.escape(optionId(active.value))}`)
    ?.scrollIntoView?.({ block: 'nearest' })
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'ArrowDown') void move(1)
  else if (event.key === 'ArrowUp') void move(-1)
  else if (event.key === 'Enter') {
    const item = flat.value[active.value]
    if (item) emit('select', item)
  } else if (event.key === 'Escape') emit('close')
  else return
  event.preventDefault()
  // Keeps Escape from also closing a sheet underneath.
  event.stopPropagation()
}

onMounted(async () => {
  returnFocus = document.activeElement as HTMLElement | null
  await nextTick()
  input.value?.focus()
})
onBeforeUnmount(() => returnFocus?.focus())
</script>

<template>
  <Teleport to="body">
    <div class="fade-in fixed inset-0 z-50 bg-ink/30" aria-hidden="true" @click="emit('close')" />
    <div
      role="dialog"
      aria-modal="true"
      :aria-label="label"
      class="pop fixed inset-x-4 top-[12vh] z-50 mx-auto flex max-h-[70vh] max-w-xl flex-col overflow-hidden rounded-3xl bg-panel shadow-[0_10px_40px_rgb(17_24_39/0.2)]"
    >
      <div class="flex items-center gap-3 border-b border-line-soft px-5 py-4">
        <svg
          viewBox="0 0 24 24"
          class="h-5 w-5 shrink-0 text-muted"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          aria-hidden="true"
        >
          <circle cx="11" cy="11" r="7" />
          <path d="M20 20l-3.5-3.5" />
        </svg>
        <input
          ref="input"
          v-model="query"
          type="search"
          role="combobox"
          aria-autocomplete="list"
          :aria-expanded="flat.length > 0"
          :aria-controls="`${id}-listbox`"
          :aria-activedescendant="flat.length ? optionId(active) : undefined"
          :aria-label="label"
          :placeholder="placeholder"
          autocomplete="off"
          spellcheck="false"
          class="min-w-0 flex-1 border-0 bg-transparent text-base text-ink outline-none"
          @keydown="onKeydown"
        />
        <kbd class="rounded-md border border-line px-1.5 py-0.5 text-xs text-muted">Esc</kbd>
      </div>
      <div
        :id="`${id}-listbox`"
        ref="listbox"
        role="listbox"
        :aria-label="label"
        :aria-busy="loading"
        class="flex flex-col gap-3 overflow-y-auto p-3"
      >
        <template v-if="loading && !flat.length">
          <div v-for="n in 3" :key="n" class="flex items-center gap-3 px-3 py-2">
            <SkeletonBlock height="28px" width="28px" />
            <div class="flex flex-1 flex-col gap-1.5">
              <SkeletonBlock height="12px" width="60%" />
              <SkeletonBlock height="10px" width="35%" />
            </div>
          </div>
        </template>
        <p v-else-if="!flat.length && emptyText" class="px-3 py-6 text-center text-sm text-muted">
          {{ emptyText }}
        </p>
        <div
          v-for="(section, si) in rows"
          :key="section.title"
          role="group"
          :aria-labelledby="`${id}-section-${si}`"
          class="flex flex-col"
        >
          <div
            :id="`${id}-section-${si}`"
            class="px-3 pb-1 text-xs font-semibold uppercase tracking-wide text-muted"
          >
            {{ section.title }}
          </div>
          <div
            v-for="{ item, index } in section.items"
            :id="optionId(index)"
            :key="item.id"
            role="option"
            :aria-selected="index === active"
            :class="index === active ? 'bg-hover' : ''"
            class="fade-up flex cursor-pointer items-center gap-3 rounded-xl px-3 py-2"
            :style="{ '--i': index }"
            @mousemove="active = index"
            @click="emit('select', item)"
          >
            <span
              class="flex h-7 w-7 shrink-0 items-center justify-center text-lg"
              aria-hidden="true"
              >{{ item.icon }}</span
            >
            <span class="flex min-w-0 flex-1 flex-col">
              <span class="truncate text-sm font-medium text-ink">{{ item.title }}</span>
              <span v-if="item.subtitle" class="truncate text-xs text-muted">{{
                item.subtitle
              }}</span>
            </span>
            <span v-if="item.hint" class="shrink-0 text-xs font-medium text-muted">{{
              item.hint
            }}</span>
          </div>
        </div>
      </div>
      <div class="flex gap-4 border-t border-line-soft px-5 py-2.5 text-xs text-muted">
        <span>↑↓ to move</span>
        <span>↵ to open</span>
      </div>
    </div>
  </Teleport>
</template>
