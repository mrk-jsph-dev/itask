<script setup lang="ts">
import { computed, ref } from 'vue'
import { VueDraggable } from 'vue-draggable-plus'

import type { ChecklistItem } from '../api'
import { useCardDetailStore } from '../cardDetailStore'
import { errorText, useBoardsStore } from '../store'
import { toast } from '@/shared/ui/toast'

const props = defineProps<{ editable: boolean }>()

const detail = useCardDetailStore()
const boards = useBoardsStore()

const draft = ref('')
const adding = ref(false)
const editingId = ref<string | null>(null)
const editText = ref('')
/** Order before a drag, restored if the server refuses the drop. */
let beforeDrag: ChecklistItem[] = []

const items = computed<ChecklistItem[]>({
  get: () => detail.checklist,
  set: (value) => (detail.checklist = value),
})

const memberName = (userId: string | null) =>
  boards.members.find((m) => m.userId === userId)?.displayName ?? null

async function add() {
  const text = draft.value.trim()
  if (!text || adding.value) return
  adding.value = true
  try {
    await detail.addItem(text)
    draft.value = ''
  } catch (e) {
    toast(errorText(e, "Couldn't add the item."), 'error')
  } finally {
    adding.value = false
  }
}

function startEdit(item: ChecklistItem) {
  if (!props.editable) return
  editingId.value = item.id
  editText.value = item.text
}

async function saveEdit(item: ChecklistItem) {
  const text = editText.value.trim()
  editingId.value = null
  if (!text || text === item.text) return
  try {
    await detail.updateItem(item.id, { text })
  } catch (e) {
    toast(errorText(e, "Couldn't rename the item."), 'error')
  }
}

async function assign(item: ChecklistItem, event: Event) {
  const value = (event.target as HTMLSelectElement).value
  try {
    await detail.updateItem(item.id, { assigneeId: value || null })
  } catch (e) {
    toast(errorText(e, "Couldn't assign the item."), 'error')
  }
}

function onDragStart() {
  beforeDrag = [...detail.checklist]
}

function onDrop(event: { oldDraggableIndex?: number; newDraggableIndex?: number }) {
  const { oldDraggableIndex: from, newDraggableIndex: to } = event
  if (from === undefined || to === undefined || from === to) return
  void detail.commitDraggedItem(to, beforeDrag)
}
</script>

<template>
  <section class="flex flex-col gap-3" aria-labelledby="checklist-title">
    <div class="flex items-center gap-2.5">
      <h3 id="checklist-title" class="text-base font-semibold">☑️ Subtasks</h3>
      <span v-if="items.length" class="text-[13px] text-muted"
        >{{ detail.done }}/{{ items.length }}</span
      >
      <div
        v-if="items.length"
        class="h-1.5 flex-1 overflow-hidden rounded-full bg-line-soft"
        role="progressbar"
        :aria-valuenow="detail.progress"
        aria-valuemin="0"
        aria-valuemax="100"
        aria-label="Checklist progress"
      >
        <div
          class="h-full rounded-full bg-accent transition-[width] duration-300"
          :style="{ width: `${detail.progress}%` }"
        />
      </div>
    </div>

    <VueDraggable
      v-model="items"
      tag="ul"
      class="flex flex-col rounded-2xl border-[1.5px] border-dashed border-line p-1.5 empty:hidden"
      :disabled="!editable"
      handle=".item-handle"
      ghost-class="drag-ghost"
      :animation="150"
      aria-label="Checklist"
      @start="onDragStart"
      @end="onDrop"
    >
      <li
        v-for="(item, i) in items"
        :key="item.id"
        class="group flex min-h-11 items-center gap-2.5 rounded-[10px] px-2.5 hover:bg-hover"
      >
        <span
          v-if="editable"
          class="item-handle cursor-grab text-muted opacity-40 group-hover:opacity-100"
          aria-hidden="true"
          >⠿</span
        >
        <input
          type="checkbox"
          class="pop-check h-[18px] w-[18px] shrink-0 accent-accent"
          :checked="item.isDone"
          :disabled="!editable"
          :aria-label="`${item.isDone ? 'Uncheck' : 'Check'} ${item.text}`"
          @change="detail.toggleItem(item.id)"
        />
        <input
          v-if="editingId === item.id"
          v-model="editText"
          maxlength="300"
          class="h-8 min-w-0 flex-1 rounded-lg border border-line px-2 text-[15px]"
          :aria-label="`Rename ${item.text}`"
          @keydown.enter.prevent="saveEdit(item)"
          @keydown.esc.stop="editingId = null"
          @blur="saveEdit(item)"
        />
        <button
          v-else
          type="button"
          class="strike min-w-0 flex-1 text-left text-sm break-words"
          :class="item.isDone ? 'text-[#6B7280] line-through' : 'text-ink'"
          :disabled="!editable"
          @click="startEdit(item)"
        >
          {{ item.text }}
        </button>
        <span v-if="!editable && item.assigneeId" class="shrink-0 text-xs text-muted">{{
          memberName(item.assigneeId)
        }}</span>
        <template v-if="editable">
          <select
            class="h-7 max-w-28 shrink-0 rounded-lg border border-transparent bg-transparent text-xs text-muted hover:border-line"
            :value="item.assigneeId ?? ''"
            :aria-label="`Assignee for ${item.text}`"
            @change="assign(item, $event)"
          >
            <option value="">Unassigned</option>
            <option v-for="m in boards.members" :key="m.userId" :value="m.userId">
              {{ m.displayName || m.email }}
            </option>
          </select>
          <button
            type="button"
            class="ghost h-7 w-7 shrink-0 rounded-lg text-xs text-muted disabled:opacity-30"
            :disabled="i === 0"
            :aria-label="`Move ${item.text} up`"
            @click="detail.moveChecklistItem(i, i - 1)"
          >
            ↑
          </button>
          <button
            type="button"
            class="ghost h-7 w-7 shrink-0 rounded-lg text-xs text-muted disabled:opacity-30"
            :disabled="i === items.length - 1"
            :aria-label="`Move ${item.text} down`"
            @click="detail.moveChecklistItem(i, i + 1)"
          >
            ↓
          </button>
          <button
            type="button"
            class="ghost h-7 w-7 shrink-0 rounded-lg text-xs text-red-700"
            :aria-label="`Delete ${item.text}`"
            @click="detail.removeItem(item.id)"
          >
            ✕
          </button>
        </template>
      </li>
    </VueDraggable>
    <p v-if="!items.length && !editable" class="text-sm text-muted">No subtasks.</p>

    <form v-if="editable" class="flex gap-2" @submit.prevent="add">
      <input
        v-model="draft"
        maxlength="300"
        placeholder="Add a subtask"
        aria-label="New subtask"
        class="h-10 min-w-0 flex-1 rounded-[var(--radius-control)] border border-line bg-panel px-3 text-[15px]"
      />
      <button
        type="submit"
        class="press h-10 rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-50"
        :disabled="!draft.trim() || adding"
      >
        Add
      </button>
    </form>
  </section>
</template>
