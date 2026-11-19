<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'

import UserAvatar from '@/shared/ui/UserAvatar.vue'
import type { CardPriority, Label, Member } from '../api'
import { priorities } from '../cardOptions'
import { dueFilters, dueLabels, unassigned, type BoardFilter } from '../filters'

const props = defineProps<{
  filter: BoardFilter
  labels: Label[]
  members: Pick<Member, 'userId' | 'displayName' | 'avatarColor'>[]
}>()
const emit = defineEmits<{ change: [filter: BoardFilter]; close: [] }>()

const text = ref(props.filter.q)
const input = useTemplateRef('input')
let timer: ReturnType<typeof setTimeout> | undefined

onMounted(() => input.value?.focus())
// Closing before the debounce fires still applies what was typed.
onBeforeUnmount(() => {
  clearTimeout(timer)
  if (text.value !== props.filter.q) emit('change', { ...props.filter, q: text.value })
})

watch(
  () => props.filter.q,
  (q) => {
    if (q !== text.value.trim()) text.value = q
  },
)

function onText() {
  clearTimeout(timer)
  timer = setTimeout(() => emit('change', { ...props.filter, q: text.value }), 250)
}

type ListField = 'labels' | 'assignees' | 'due' | 'priority'

function toggle<K extends ListField>(field: K, value: BoardFilter[K][number]) {
  const current = props.filter[field] as string[]
  const next = current.includes(value) ? current.filter((v) => v !== value) : [...current, value]
  emit('change', { ...props.filter, [field]: next })
}

const on = (field: ListField, value: string) => (props.filter[field] as string[]).includes(value)

function onKeydown(event: KeyboardEvent) {
  if (event.key !== 'Escape') return
  event.stopPropagation()
  emit('close')
}

const chip =
  'press flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-[13px] font-medium'
const chipState = (active: boolean) =>
  active ? 'border-accent bg-[#EFF4FF] text-accent' : 'border-line text-[#374151] hover:bg-hover'
const priorityChoices = priorities as readonly CardPriority[]
</script>

<template>
  <div
    role="dialog"
    aria-label="Filter tasks"
    class="pop absolute top-11 right-0 z-30 flex w-[min(340px,calc(100vw-32px))] flex-col gap-4 rounded-[var(--radius-control)] border border-line-soft bg-panel p-4 text-sm shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
    @keydown="onKeydown"
  >
    <label class="flex flex-col gap-1.5">
      <span class="text-xs font-semibold text-muted">Text or key</span>
      <input
        ref="input"
        v-model="text"
        type="search"
        maxlength="100"
        placeholder="e.g. login or RM-12"
        class="h-10 rounded-[var(--radius-control)] border border-line px-3"
        @input="onText"
      />
    </label>

    <fieldset class="flex flex-col gap-2">
      <legend class="mb-2 text-xs font-semibold text-muted">Due</legend>
      <div class="flex flex-wrap gap-1.5">
        <button
          v-for="d in dueFilters"
          :key="d"
          type="button"
          :class="[chip, chipState(on('due', d))]"
          :aria-pressed="on('due', d)"
          @click="toggle('due', d)"
        >
          {{ dueLabels[d] }}
        </button>
      </div>
    </fieldset>

    <fieldset class="flex flex-col gap-2">
      <legend class="mb-2 text-xs font-semibold text-muted">Priority</legend>
      <div class="flex flex-wrap gap-1.5">
        <button
          v-for="p in priorityChoices"
          :key="p"
          type="button"
          :class="[chip, chipState(on('priority', p))]"
          :aria-pressed="on('priority', p)"
          @click="toggle('priority', p)"
        >
          {{ p === 'None' ? 'No priority' : p }}
        </button>
      </div>
    </fieldset>

    <fieldset v-if="labels.length" class="flex flex-col gap-2">
      <legend class="mb-2 text-xs font-semibold text-muted">Labels</legend>
      <div class="flex flex-wrap gap-1.5">
        <button
          v-for="label in labels"
          :key="label.id"
          type="button"
          :class="[chip, chipState(on('labels', label.id))]"
          :aria-pressed="on('labels', label.id)"
          @click="toggle('labels', label.id)"
        >
          <span
            class="h-2.5 w-2.5 rounded-full"
            :style="{ backgroundColor: label.color }"
            aria-hidden="true"
          />
          <span v-if="label.emoji" aria-hidden="true">{{ label.emoji }}</span>
          {{ label.name }}
        </button>
      </div>
    </fieldset>

    <fieldset class="flex flex-col gap-2">
      <legend class="mb-2 text-xs font-semibold text-muted">Assignees</legend>
      <div class="flex flex-wrap gap-1.5">
        <button
          v-for="m in members"
          :key="m.userId"
          type="button"
          :class="[chip, chipState(on('assignees', m.userId))]"
          :aria-pressed="on('assignees', m.userId)"
          @click="toggle('assignees', m.userId)"
        >
          <UserAvatar :name="m.displayName" :color="m.avatarColor" :size="18" />
          {{ m.displayName }}
        </button>
        <button
          type="button"
          :class="[chip, chipState(on('assignees', unassigned))]"
          :aria-pressed="on('assignees', unassigned)"
          @click="toggle('assignees', unassigned)"
        >
          Unassigned
        </button>
      </div>
    </fieldset>

    <label class="flex items-center gap-2.5">
      <input
        type="checkbox"
        class="h-4 w-4 accent-[var(--color-accent)]"
        :checked="filter.hideCompleted"
        @change="emit('change', { ...filter, hideCompleted: !filter.hideCompleted })"
      />
      Hide completed tasks
    </label>
  </div>
</template>
