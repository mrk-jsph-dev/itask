<script setup lang="ts">
import { computed, ref, useId, useTemplateRef } from 'vue'
import { VueDraggable, type SortableEvent } from 'vue-draggable-plus'

import { useMenu } from '@/shared/ui/menu'

import type { BoardList, Card, ListInput } from '../api'
import CardTile from './CardTile.vue'
import ListForm from './ListForm.vue'
import NewCardForm from './NewCardForm.vue'

const props = defineProps<{
  list: BoardList
  editable: boolean
  canMoveLeft: boolean
  canMoveRight: boolean
  /** Every active list, for the card menu's "Move to list…". */
  allLists: Pick<BoardList, 'id' | 'name' | 'emoji'>[]
  /** Card that just dropped, for a brief glow. */
  settledCardId?: string | null
}>()
const emit = defineEmits<{
  rename: [input: ListInput, done: (ok: boolean) => void]
  move: [delta: -1 | 1]
  archive: []
  createCard: [title: string, done: (ok: boolean) => void]
  openCard: [cardId: string]
  moveCard: [cardId: string, listId: string, index: number]
  toggleComplete: [card: Card]
  archiveCard: [cardId: string]
  dragStart: []
  dropCard: [cardId: string]
}>()

/** The list's cards; drag and drop writes the new order back through v-model. */
const cards = defineModel<Card[]>('cards', { required: true })

const targets = computed(() => props.allLists.filter((l) => l.id !== props.list.id))
const overLimit = computed(() => !!props.list.wipLimit && cards.value.length > props.list.wipLimit)

function onCardDrop(event: SortableEvent) {
  const cardId = (event.item as HTMLElement).dataset.cardId
  const moved = event.from !== event.to || event.oldDraggableIndex !== event.newDraggableIndex
  if (cardId && moved) emit('dropCard', cardId)
}

const newCard = useTemplateRef('newCard')
/** Opens the add-task form (header `+`, board "+ New task"). */
function addCard() {
  void newCard.value?.start()
}
defineExpose({ addCard })

const menuId = useId()
const editing = ref(false)
const saving = ref(false)
const menu = useTemplateRef('menu')
const trigger = useTemplateRef('trigger')
const {
  open: menuOpen,
  close: closeMenu,
  toggle: openMenu,
  onKeydown: onMenuKey,
} = useMenu(menu, trigger)

function pick(action: 'rename' | 'left' | 'right' | 'archive') {
  closeMenu(action === 'left' || action === 'right')
  if (action === 'rename') editing.value = true
  else if (action === 'left') emit('move', -1)
  else if (action === 'right') emit('move', 1)
  else emit('archive')
}

function save(input: ListInput) {
  saving.value = true
  emit('rename', input, (ok) => {
    saving.value = false
    if (ok) editing.value = false
  })
}
</script>

<template>
  <section class="flex min-w-0 flex-col gap-2.5" :aria-label="`${list.name} list`">
    <ListForm
      v-if="editing"
      submit-label="Save"
      :busy="saving"
      :initial="{ name: list.name, emoji: list.emoji, wipLimit: list.wipLimit }"
      @submit="save"
      @cancel="editing = false"
    />
    <header
      v-else
      class="list-handle relative flex h-10 items-center gap-2 rounded-xl px-1 pt-1"
      :class="editable ? 'cursor-grab active:cursor-grabbing' : ''"
    >
      <span v-if="list.emoji" class="text-xl" aria-hidden="true">{{ list.emoji }}</span>
      <h2 class="min-w-0 truncate text-base font-semibold">{{ list.name }}</h2>
      <span
        class="text-[13px]"
        :class="overLimit ? 'font-semibold text-red-700' : 'text-muted'"
        :title="list.wipLimit ? `WIP limit ${list.wipLimit}` : undefined"
        :aria-label="`${cards.length} cards${list.wipLimit ? `, limit ${list.wipLimit}` : ''}`"
        >{{ cards.length }}<template v-if="list.wipLimit">/{{ list.wipLimit }}</template></span
      >
      <template v-if="editable">
        <button
          ref="trigger"
          type="button"
          class="ghost ml-auto h-[30px] w-[30px] rounded-lg font-bold text-muted"
          aria-haspopup="menu"
          :aria-expanded="menuOpen"
          :aria-controls="menuId"
          :aria-label="`${list.name} list menu`"
          @click.stop="openMenu"
        >
          ···
        </button>
        <button
          type="button"
          class="ghost h-[30px] w-[30px] rounded-lg text-lg text-[#374151]"
          :aria-label="`Add task to ${list.name} list`"
          @click.stop="addCard"
        >
          +
        </button>
        <div
          v-if="menuOpen"
          :id="menuId"
          ref="menu"
          role="menu"
          :aria-label="`${list.name} list actions`"
          class="pop absolute top-11 right-0 z-20 flex w-44 flex-col rounded-[var(--radius-control)] border border-line-soft bg-panel p-1 text-sm shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
          @keydown="onMenuKey"
        >
          <button
            type="button"
            role="menuitem"
            class="ghost rounded-lg px-3 py-2 text-left"
            @click="pick('rename')"
          >
            ✏️ Rename
          </button>
          <button
            type="button"
            role="menuitem"
            class="ghost rounded-lg px-3 py-2 text-left disabled:opacity-40"
            :disabled="!canMoveLeft"
            @click="pick('left')"
          >
            ← Move left
          </button>
          <button
            type="button"
            role="menuitem"
            class="ghost rounded-lg px-3 py-2 text-left disabled:opacity-40"
            :disabled="!canMoveRight"
            @click="pick('right')"
          >
            → Move right
          </button>
          <button
            type="button"
            role="menuitem"
            class="ghost rounded-lg px-3 py-2 text-left text-red-700"
            @click="pick('archive')"
          >
            🗄 Archive
          </button>
        </div>
      </template>
    </header>
    <div class="relative">
      <span
        v-if="cards.length === 0"
        class="pointer-events-none absolute inset-0 flex items-center justify-center text-[13px] text-muted"
        >No tasks yet</span
      >
      <VueDraggable
        v-model="cards"
        tag="ol"
        class="flex min-h-24 flex-col gap-2 rounded-[18px] border-[1.5px] border-dashed border-line p-2"
        :aria-label="`${list.name} cards`"
        group="cards"
        :disabled="!editable"
        draggable="[data-card-id]"
        ghost-class="drag-ghost"
        drag-class="dragging"
        filter="[role=menu], [aria-haspopup]"
        :prevent-on-filter="false"
        :animation="200"
        :force-fallback="true"
        @start="emit('dragStart')"
        @end="onCardDrop"
      >
        <li
          v-for="(card, i) in cards"
          :key="card.id"
          :data-card-id="card.id"
          class="fade-up rounded-[14px] has-[[role=menu]]:z-30"
          :class="{ settle: settledCardId === card.id }"
          :style="{ '--i': Math.min(i, 8) }"
        >
          <CardTile
            :card="card"
            :editable="editable"
            :can-move-up="i > 0"
            :can-move-down="i < cards.length - 1"
            :targets="targets"
            @open="emit('openCard', card.id)"
            @move="(delta) => emit('moveCard', card.id, list.id, i + delta)"
            @move-to="(listId) => emit('moveCard', card.id, listId, Number.MAX_SAFE_INTEGER)"
            @toggle-complete="emit('toggleComplete', card)"
            @archive="emit('archiveCard', card.id)"
          />
        </li>
      </VueDraggable>
    </div>
    <NewCardForm
      v-if="editable"
      ref="newCard"
      :list-name="list.name"
      @create="(title, done) => emit('createCard', title, done)"
    />
  </section>
</template>
