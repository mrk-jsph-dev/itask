<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'

import { aiApi } from '../api'
import { aiPriorities, quickAddCard, type QuickAddCard } from '../draft'
import { useAiStore } from '../store'
import AiButton from './AiButton.vue'

const props = defineProps<{
  boardId: string
  lists: readonly { id: string; name: string; emoji: string | null }[]
  labels: readonly { id: string; name: string; color: string; emoji: string | null }[]
  saving?: boolean
}>()
const emit = defineEmits<{ close: []; accept: [card: QuickAddCard] }>()

const ai = useAiStore()
const panel = useTemplateRef('panel')
const text = ref('')
const draft = ref<QuickAddCard | null>(null)
let returnFocus: HTMLElement | null = null

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') emit('close')
}

onMounted(async () => {
  returnFocus = document.activeElement as HTMLElement | null
  document.addEventListener('keydown', onKeydown)
  await nextTick()
  panel.value?.querySelector<HTMLElement>('input')?.focus()
})
onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown)
  returnFocus?.focus()
})

async function generate() {
  if (!text.value.trim()) return
  const result = await ai.run('quick-add', () => aiApi.quickAdd(props.boardId, text.value.trim()))
  if (!result) return
  draft.value = quickAddCard(result, props.lists, props.labels)
  await nextTick()
  panel.value?.querySelector<HTMLElement>('#ai-draft-title')?.focus()
}

function toggleLabel(id: string) {
  if (!draft.value) return
  const ids = draft.value.labelIds
  draft.value.labelIds = ids.includes(id) ? ids.filter((l) => l !== id) : [...ids, id]
}

function accept() {
  if (draft.value?.title.trim() && draft.value.listId) {
    emit('accept', { ...draft.value, title: draft.value.title.trim() })
  }
}

const field = 'h-10 w-full rounded-[12px] border border-line bg-panel px-3 text-sm'
</script>

<template>
  <Teleport to="body">
    <div
      class="fade-in fixed inset-0 z-40 flex items-center justify-center overflow-y-auto bg-scrim/40 p-4"
      @click.self="emit('close')"
    >
      <div
        ref="panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="ai-quick-add-title"
        class="fade-up my-auto flex max-h-full w-full max-w-[520px] flex-col gap-4 overflow-y-auto rounded-[24px] bg-panel p-7"
      >
        <h2 id="ai-quick-add-title" class="text-xl font-bold">✨ Quick add</h2>
        <form class="flex flex-col gap-2" @submit.prevent="generate">
          <label for="ai-quick-add-text" class="text-sm font-semibold">Describe the task</label>
          <input
            id="ai-quick-add-text"
            v-model="text"
            :class="field"
            maxlength="300"
            placeholder="Fix login bug by Friday, high"
            autocomplete="off"
          />
          <div class="flex flex-wrap items-center gap-2">
            <AiButton
              :label="draft ? 'Try again' : 'Draft task'"
              action="quick-add"
              compact
              @click="generate"
            />
            <p class="ml-auto text-xs text-muted">
              {{ ai.remaining }} of {{ ai.limit }} AI requests left today
            </p>
          </div>
        </form>

        <form
          v-if="draft"
          class="fade-up flex flex-col gap-3 rounded-[16px] border border-accent-line bg-accent-soft p-4"
          aria-label="Task draft"
          @submit.prevent="accept"
        >
          <div class="flex flex-col gap-1">
            <label for="ai-draft-title" class="text-xs font-semibold text-muted">Title</label>
            <input
              id="ai-draft-title"
              v-model="draft.title"
              :class="field"
              maxlength="200"
              required
            />
          </div>
          <div class="grid grid-cols-[repeat(auto-fill,minmax(140px,1fr))] gap-3">
            <div class="flex flex-col gap-1">
              <label for="ai-draft-list" class="text-xs font-semibold text-muted">List</label>
              <select id="ai-draft-list" v-model="draft.listId" :class="field">
                <option v-for="list in lists" :key="list.id" :value="list.id">
                  {{ list.emoji ? `${list.emoji} ` : '' }}{{ list.name }}
                </option>
              </select>
            </div>
            <div class="flex flex-col gap-1">
              <label for="ai-draft-due" class="text-xs font-semibold text-muted">Due</label>
              <input
                id="ai-draft-due"
                type="date"
                :value="draft.dueDate ?? ''"
                :class="field"
                @input="draft.dueDate = ($event.target as HTMLInputElement).value || null"
              />
            </div>
            <div class="flex flex-col gap-1">
              <label for="ai-draft-priority" class="text-xs font-semibold text-muted"
                >Priority</label
              >
              <select id="ai-draft-priority" v-model="draft.priority" :class="field">
                <option v-for="p in aiPriorities" :key="p" :value="p">{{ p }}</option>
              </select>
            </div>
          </div>
          <div v-if="labels.length" class="flex flex-col gap-1">
            <span id="ai-draft-labels" class="text-xs font-semibold text-muted">Labels</span>
            <div class="flex flex-wrap gap-1.5" role="group" aria-labelledby="ai-draft-labels">
              <button
                v-for="label in labels"
                :key="label.id"
                type="button"
                class="press flex h-8 items-center gap-1.5 rounded-full border px-3 text-xs font-semibold"
                :class="
                  draft.labelIds.includes(label.id)
                    ? 'border-transparent text-on-accent'
                    : 'border-line bg-panel text-ink'
                "
                :style="draft.labelIds.includes(label.id) ? { background: label.color } : {}"
                :aria-pressed="draft.labelIds.includes(label.id)"
                @click="toggleLabel(label.id)"
              >
                <span v-if="label.emoji" aria-hidden="true">{{ label.emoji }}</span>
                {{ label.name }}
              </button>
            </div>
          </div>
          <div class="flex justify-end gap-2.5">
            <button
              type="button"
              class="ghost h-10 rounded-full border border-line bg-panel px-4 text-sm font-semibold"
              @click="draft = null"
            >
              Discard
            </button>
            <button
              type="submit"
              class="press flex h-10 items-center gap-2 rounded-full bg-accent px-4 text-sm font-semibold text-on-accent disabled:opacity-60"
              :disabled="saving || !draft.title.trim() || !draft.listId"
            >
              <span v-if="saving" class="spinner" aria-hidden="true" />
              Add task
            </button>
          </div>
        </form>
      </div>
    </div>
  </Teleport>
</template>
