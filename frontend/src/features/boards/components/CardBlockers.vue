<script setup lang="ts">
import { computed, nextTick, ref, useId, useTemplateRef } from 'vue'

import { toast } from '@/shared/ui/toast'
import type { Card } from '../api'
import { blockerCandidates } from '../blockers'
import { useCardDetailStore } from '../cardDetailStore'
import { useListsStore } from '../listsStore'
import { errorText } from '../store'

const props = defineProps<{ cardId: string; editable: boolean }>()

const detail = useCardDetailStore()
const lists = useListsStore()

const listboxId = useId()
const input = useTemplateRef('input')
const picking = ref(false)
const query = ref('')
const active = ref(0)
const saving = ref(false)

const boardCards = computed(() => (lists.lists ?? []).flatMap((l) => l.cards))
const matches = computed(() =>
  blockerCandidates(
    boardCards.value,
    props.cardId,
    detail.blockedBy.map((b) => b.id),
    query.value,
  ),
)

async function startPicking() {
  picking.value = true
  query.value = ''
  active.value = 0
  await nextTick()
  input.value?.focus()
}

function stopPicking() {
  picking.value = false
}

async function pick(card: Card) {
  if (saving.value) return
  saving.value = true
  try {
    await detail.addBlocker(card)
    stopPicking()
  } catch (e) {
    toast(errorText(e, "Couldn't add the blocker."), 'error')
  } finally {
    saving.value = false
  }
}

function onKeydown(e: KeyboardEvent) {
  const count = matches.value.length
  if (e.key === 'Escape') {
    e.stopPropagation()
    stopPicking()
  } else if (e.key === 'ArrowDown' && count) {
    e.preventDefault()
    active.value = (active.value + 1) % count
  } else if (e.key === 'ArrowUp' && count) {
    e.preventDefault()
    active.value = (active.value - 1 + count) % count
  } else if (e.key === 'Enter' && matches.value[active.value]) {
    e.preventDefault()
    void pick(matches.value[active.value]!)
  }
}

function onInput() {
  active.value = 0
}
</script>

<template>
  <section class="flex flex-col gap-3" aria-labelledby="blockers-title">
    <div class="flex items-center gap-2">
      <h3 id="blockers-title" class="text-base font-semibold">🔒 Blocked by</h3>
      <button
        v-if="editable && !picking"
        type="button"
        class="ghost press ml-auto rounded-lg px-2.5 py-1 text-sm font-semibold text-accent"
        @click="startPicking"
      >
        + Add blocker
      </button>
    </div>

    <p v-if="!detail.blockedBy.length && !picking" class="text-sm text-muted">
      {{ editable ? 'Nothing. Add a card this one waits on.' : 'Nothing.' }}
    </p>

    <ul v-if="detail.blockedBy.length" class="flex flex-col gap-1.5" aria-label="Blockers">
      <li
        v-for="(blocker, i) in detail.blockedBy"
        :key="blocker.id"
        class="fade-up flex items-center gap-2.5 rounded-xl border border-line-soft px-3 py-2 text-sm"
        :style="{ '--i': i }"
      >
        <span
          class="flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] font-bold"
          :class="
            blocker.completed || blocker.archived
              ? 'bg-[#DCFCE7] text-[#15803D]'
              : 'bg-[#FEF2F2] text-[#B91C1C]'
          "
          aria-hidden="true"
          >{{ blocker.completed || blocker.archived ? '✓' : '!' }}</span
        >
        <span class="shrink-0 text-xs font-semibold text-muted">{{ blocker.key }}</span>
        <span
          class="min-w-0 flex-1 truncate"
          :class="{ 'text-muted line-through': blocker.completed || blocker.archived }"
          >{{ blocker.title }}</span
        >
        <span class="sr-only">{{
          blocker.archived ? '(archived)' : blocker.completed ? '(done)' : '(open)'
        }}</span>
        <button
          v-if="editable"
          type="button"
          class="ghost press rounded-lg px-2 py-0.5 text-muted"
          :aria-label="`Remove blocker ${blocker.key}`"
          @click="detail.removeBlocker(blocker.id)"
        >
          ×
        </button>
      </li>
    </ul>

    <div v-if="picking" class="flex flex-col gap-1 rounded-2xl border border-line-soft p-2">
      <input
        ref="input"
        v-model="query"
        type="search"
        class="rounded-xl border border-line px-3 py-2 text-sm"
        placeholder="Search this board by key or title"
        aria-label="Find a card this one waits on"
        role="combobox"
        aria-autocomplete="list"
        :aria-expanded="true"
        :aria-controls="listboxId"
        :aria-activedescendant="matches.length ? `${listboxId}-${active}` : undefined"
        @input="onInput"
        @keydown="onKeydown"
        @blur="query || stopPicking()"
      />
      <ul :id="listboxId" role="listbox" class="flex max-h-60 flex-col overflow-y-auto">
        <li
          v-for="(card, index) in matches"
          :id="`${listboxId}-${index}`"
          :key="card.id"
          role="option"
          :aria-selected="index === active"
          class="flex cursor-pointer items-center gap-3 rounded-xl px-3 py-2"
          :class="index === active ? 'bg-hover' : ''"
          @mousedown.prevent="pick(card)"
          @mouseenter="active = index"
        >
          <span class="shrink-0 text-xs font-semibold text-muted">{{ card.key }}</span>
          <span class="min-w-0 flex-1 truncate text-sm font-medium text-ink">{{ card.title }}</span>
          <span v-if="card.completedAt" class="shrink-0 text-xs text-muted">done</span>
        </li>
        <li v-if="!matches.length" class="px-3 py-3 text-center text-sm text-muted">
          No matching cards.
        </li>
      </ul>
      <button
        type="button"
        class="ghost self-end rounded-lg px-2.5 py-1 text-sm text-muted"
        @click="stopPicking"
      >
        Cancel
      </button>
    </div>
  </section>
</template>
