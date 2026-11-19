<script setup lang="ts">
import { computed, ref, useId, useTemplateRef } from 'vue'

import { useMenu } from '@/shared/ui/menu'
import { toast } from '@/shared/ui/toast'
import type { BoardList, Recurrence, RecurrenceInput } from '../api'
import {
  defaultRecurrence,
  describeRecurrence,
  isValid,
  nextRunText,
  recurrenceKinds,
  toInput,
  weekdayOptions,
} from '../recurrence'
import { useCardDetailStore } from '../cardDetailStore'
import { errorText } from '../store'

const props = defineProps<{
  editable: boolean
  dueAt: string | null
  lists: Pick<BoardList, 'id' | 'name' | 'emoji'>[]
  loading: boolean
}>()

const detail = useCardDetailStore()
const id = useId()
const panel = useTemplateRef('panel')
const trigger = useTemplateRef('trigger')
const { open, close, toggle } = useMenu(panel, trigger)

const rule = computed<Recurrence | null>(() => detail.recurrence)
const form = ref<RecurrenceInput>(defaultRecurrence(null, ''))
const saving = ref(false)

async function openPanel() {
  form.value = rule.value
    ? { ...rule.value, weekdays: [...rule.value.weekdays] }
    : defaultRecurrence(props.dueAt, props.lists[0]?.id ?? '')
  if (form.value.dayOfMonth === null) form.value.dayOfMonth = new Date().getDate()
  if (!props.lists.some((l) => l.id === form.value.targetListId))
    form.value.targetListId = props.lists[0]?.id ?? ''
  await toggle()
  panel.value?.querySelector<HTMLElement>('[aria-checked="true"]')?.focus()
}

function toggleDay(day: number) {
  const days = form.value.weekdays
  form.value.weekdays = days.includes(day) ? days.filter((d) => d !== day) : [...days, day]
}

async function save() {
  saving.value = true
  try {
    await detail.setRecurrence(toInput(form.value))
    close(true)
    toast(`🔁 ${describeRecurrence(form.value)}.`)
  } catch (e) {
    toast(errorText(e, "Couldn't save the repeat."), 'error')
  } finally {
    saving.value = false
  }
}

async function stop() {
  saving.value = true
  try {
    await detail.clearRecurrence()
    close(true)
    toast('This task no longer repeats.')
  } catch (e) {
    toast(errorText(e, "Couldn't stop the repeat."), 'error')
  } finally {
    saving.value = false
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.key !== 'Escape') return
  event.stopPropagation()
  close(true)
}

const segment = (active: boolean) =>
  active
    ? 'bg-panel font-semibold text-ink shadow-[0_1px_2px_rgb(17_24_39/0.08)]'
    : 'font-medium text-muted'
const chip = (active: boolean) =>
  active ? 'border-accent bg-[#EFF4FF] text-accent' : 'border-line text-[#374151] hover:bg-hover'
</script>

<template>
  <div class="relative">
    <span v-if="loading" class="text-muted">…</span>
    <button
      v-else-if="editable"
      ref="trigger"
      type="button"
      class="ghost flex min-h-[30px] items-center gap-1.5 rounded-lg px-2.5 text-left text-[13px] font-medium"
      :class="
        rule ? 'bg-[#EFF4FF] text-accent-strong' : 'border border-dashed border-line text-[#374151]'
      "
      aria-haspopup="dialog"
      :aria-expanded="open"
      :aria-controls="open ? id : undefined"
      @click.stop="open ? close() : openPanel()"
    >
      <span aria-hidden="true">🔁</span>
      <span>{{ rule ? describeRecurrence(rule) : "Doesn't repeat" }}</span>
      <span class="sr-only">, change repeat</span>
    </button>
    <span v-else class="inline-flex items-center gap-1.5" :class="{ 'text-muted': !rule }">
      <span v-if="rule" aria-hidden="true">🔁</span>
      {{ rule ? describeRecurrence(rule) : "Doesn't repeat" }}
    </span>
    <p v-if="rule && !loading" class="mt-0.5 px-2.5 text-xs text-muted">{{ nextRunText(rule) }}</p>

    <form
      v-if="open"
      :id="id"
      ref="panel"
      role="dialog"
      aria-label="Repeat task"
      class="pop absolute top-10 left-0 z-30 flex w-[min(320px,calc(100vw-32px))] flex-col gap-4 rounded-[var(--radius-control)] border border-line-soft bg-panel p-4 text-sm shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
      @keydown="onKeydown"
      @submit.prevent="save"
    >
      <div role="radiogroup" aria-label="How often" class="flex rounded-[12px] bg-hover p-1">
        <button
          v-for="kind in recurrenceKinds"
          :key="kind.value"
          type="button"
          role="radio"
          :aria-checked="form.kind === kind.value"
          class="h-8 flex-1 rounded-[10px] px-3"
          :class="segment(form.kind === kind.value)"
          @click="form.kind = kind.value"
        >
          {{ kind.label }}
        </button>
      </div>

      <fieldset v-if="form.kind === 'Weekly'" class="flex flex-col gap-2">
        <legend class="mb-1.5 text-xs font-semibold text-muted">On</legend>
        <div class="flex flex-wrap gap-1.5">
          <button
            v-for="day in weekdayOptions"
            :key="day.value"
            type="button"
            :aria-pressed="form.weekdays.includes(day.value)"
            :aria-label="day.long"
            class="press h-8 min-w-11 rounded-full border px-2.5 text-[13px] font-medium"
            :class="chip(form.weekdays.includes(day.value))"
            @click="toggleDay(day.value)"
          >
            {{ day.short }}
          </button>
        </div>
      </fieldset>

      <label v-else-if="form.kind === 'Monthly'" class="flex flex-col gap-1.5">
        <span class="text-xs font-semibold text-muted">Day of the month</span>
        <input
          v-model.number="form.dayOfMonth"
          type="number"
          min="1"
          max="31"
          required
          class="h-10 w-24 rounded-[var(--radius-control)] border border-line px-3"
        />
        <span v-if="(form.dayOfMonth ?? 0) > 28" class="text-xs text-muted">
          Shorter months use their last day.
        </span>
      </label>

      <label class="flex flex-col gap-1.5">
        <span class="text-xs font-semibold text-muted">New copy goes to</span>
        <select
          v-model="form.targetListId"
          class="h-10 rounded-[var(--radius-control)] border border-line bg-panel px-3"
        >
          <option v-for="list in lists" :key="list.id" :value="list.id">
            {{ list.emoji ? `${list.emoji} ` : '' }}{{ list.name }}
          </option>
        </select>
      </label>

      <p class="text-xs leading-relaxed text-muted">
        A fresh copy (checklist unticked, dates moved on) appears on each date, or as soon as this
        one is marked complete.
      </p>

      <div class="flex flex-wrap items-center justify-end gap-2">
        <button
          v-if="rule"
          type="button"
          class="ghost mr-auto h-9 rounded-full px-3 text-sm font-semibold text-[#B91C1C]"
          :disabled="saving"
          @click="stop"
        >
          Stop repeating
        </button>
        <button
          type="button"
          class="ghost h-9 rounded-full border border-line px-4 text-sm font-semibold"
          @click="close(true)"
        >
          Cancel
        </button>
        <button
          type="submit"
          class="press h-9 rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-60"
          :disabled="saving || !isValid(form) || !form.targetListId"
        >
          Save
        </button>
      </div>
    </form>
  </div>
</template>
