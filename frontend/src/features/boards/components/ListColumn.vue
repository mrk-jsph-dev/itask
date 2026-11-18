<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, useId, useTemplateRef } from 'vue'

import type { BoardList, ListInput } from '../api'
import ListForm from './ListForm.vue'

defineProps<{
  list: BoardList
  editable: boolean
  canMoveLeft: boolean
  canMoveRight: boolean
}>()
const emit = defineEmits<{
  rename: [input: ListInput, done: (ok: boolean) => void]
  move: [delta: -1 | 1]
  archive: []
}>()

const menuId = useId()
const menuOpen = ref(false)
const editing = ref(false)
const saving = ref(false)
const menu = useTemplateRef('menu')
const trigger = useTemplateRef('trigger')

function closeMenu(focusTrigger = false) {
  menuOpen.value = false
  if (focusTrigger) trigger.value?.focus()
}

async function openMenu() {
  menuOpen.value = !menuOpen.value
  if (menuOpen.value) {
    await nextTick()
    menu.value?.querySelector<HTMLButtonElement>('[role="menuitem"]:not(:disabled)')?.focus()
  }
}

function onMenuKey(event: KeyboardEvent) {
  const items = [
    ...(menu.value?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)') ?? []),
  ]
  const index = items.indexOf(document.activeElement as HTMLButtonElement)
  if (event.key === 'ArrowDown') items[(index + 1) % items.length]?.focus()
  else if (event.key === 'ArrowUp') items[(index - 1 + items.length) % items.length]?.focus()
  else if (event.key === 'Escape') closeMenu(true)
  else return
  event.preventDefault()
}

function closeOnOutside(event: MouseEvent) {
  const target = event.target as Node
  if (menuOpen.value && !menu.value?.contains(target) && !trigger.value?.contains(target)) {
    closeMenu()
  }
}

onMounted(() => document.addEventListener('click', closeOnOutside))
onBeforeUnmount(() => document.removeEventListener('click', closeOnOutside))

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
      class="list-handle relative flex h-10 items-center gap-2 rounded-xl px-1"
      :class="editable ? 'cursor-grab active:cursor-grabbing' : ''"
    >
      <span v-if="list.emoji" class="text-lg" aria-hidden="true">{{ list.emoji }}</span>
      <h2 class="min-w-0 truncate text-[15px] font-semibold">{{ list.name }}</h2>
      <span
        v-if="list.wipLimit"
        class="text-[13px] text-muted"
        :title="`WIP limit ${list.wipLimit}`"
        >max {{ list.wipLimit }}</span
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
    <div
      class="flex min-h-24 flex-col items-center justify-center gap-2 rounded-[18px] border-[1.5px] border-dashed border-line p-2 text-[13px] text-muted"
    >
      <span>No tasks yet</span>
    </div>
  </section>
</template>
