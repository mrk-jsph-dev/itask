<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'
import { useRouter } from 'vue-router'

import AiGoalBoard from '@/features/ai/components/AiGoalBoard.vue'
import type { GoalBoardContent } from '@/features/ai/draft'
import { useAiStore } from '@/features/ai/store'
import { useAuthStore } from '@/features/auth/store'
import type { BoardTemplate } from '../api'
import BoardDetailsFields from './BoardDetailsFields.vue'
import TemplateGallery from './TemplateGallery.vue'
import { boardColors } from '../options'
import { errorText, useBoardsStore } from '../store'

const emit = defineEmits<{ close: [] }>()

const store = useBoardsStore()
const ai = useAiStore()
const auth = useAuthStore()
/** "goal" swaps the template form for the AI board-from-goal draft. */
const mode = ref<'template' | 'goal'>('template')
const router = useRouter()
const panel = useTemplateRef('panel')

const name = ref('')
const emoji = ref<string | null>('📋')
const description = ref<string | null>('')
const color = ref<string>(boardColors[0])
const templateId = ref<string | null>(null)
const template = ref<BoardTemplate | null>(null)
const saving = ref(false)
const error = ref<string | null>(null)
let returnFocus: HTMLElement | null = null

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') emit('close')
}

onMounted(async () => {
  void ai.ensureLoaded(auth.user?.id)
  returnFocus = document.activeElement as HTMLElement | null
  document.addEventListener('keydown', onKeydown)
  await nextTick()
  panel.value?.querySelector<HTMLElement>('input')?.focus()
})
onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown)
  returnFocus?.focus()
})

/** A template brings its own emoji and colour; the user can still change both. */
function onPick(picked: BoardTemplate | null) {
  template.value = picked
  emoji.value = picked?.emoji ?? '📋'
  color.value = picked?.color ?? boardColors[0]
}

async function createFromGoal(content: GoalBoardContent) {
  saving.value = true
  error.value = null
  try {
    const created = await store.createFromContent({ ...content, color: color.value })
    emit('close')
    await router.push({ name: 'board', params: { boardId: created.id } })
  } catch (e) {
    error.value = errorText(e)
  } finally {
    saving.value = false
  }
}

async function submit() {
  saving.value = true
  error.value = null
  try {
    const created = await store.create(
      {
        name: name.value.trim(),
        emoji: emoji.value,
        description: description.value?.trim() || null,
        color: color.value,
      },
      templateId.value,
    )
    emit('close')
    await router.push({ name: 'board', params: { boardId: created.id } })
  } catch (e) {
    error.value = errorText(e)
  } finally {
    saving.value = false
  }
}
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
        aria-labelledby="new-board-title"
        class="fade-up my-auto flex max-h-full w-full max-w-[600px] flex-col gap-[18px] overflow-y-auto rounded-[24px] bg-panel p-7"
      >
        <div class="flex flex-wrap items-center gap-3">
          <h2 id="new-board-title" class="text-xl font-bold">New board</h2>
          <div
            v-if="ai.enabled"
            role="tablist"
            aria-label="Start from"
            class="ml-auto flex rounded-[12px] bg-hover p-1"
          >
            <button
              v-for="tab in [
                { value: 'template', label: 'Template' },
                { value: 'goal', label: '✨ From a goal' },
              ] as const"
              :key="tab.value"
              type="button"
              role="tab"
              :aria-selected="mode === tab.value"
              class="h-8 rounded-[10px] px-3 text-sm"
              :class="
                mode === tab.value
                  ? 'bg-panel font-semibold text-ink shadow-card'
                  : 'font-medium text-muted'
              "
              @click="mode = tab.value"
            >
              {{ tab.label }}
            </button>
          </div>
        </div>
        <template v-if="mode === 'goal'">
          <AiGoalBoard :saving="saving" @accept="createFromGoal" />
          <p v-if="error" role="alert" class="text-sm text-danger">{{ error }}</p>
          <div class="flex justify-end">
            <button
              type="button"
              class="ghost h-11 rounded-full border border-line px-[18px] font-semibold"
              @click="emit('close')"
            >
              Cancel
            </button>
          </div>
        </template>
        <form v-else class="flex flex-col gap-4" @submit.prevent="submit">
          <TemplateGallery v-model="templateId" @pick="onPick" />
          <p v-if="template" class="-mt-1 text-[13px] text-muted">
            <span class="font-semibold text-ink">Lists:</span> {{ template.lists.join(' · ') }}
          </p>
          <BoardDetailsFields
            v-model:name="name"
            v-model:emoji="emoji"
            v-model:description="description"
            v-model:color="color"
          />
          <p v-if="error" role="alert" class="text-sm text-danger">{{ error }}</p>
          <div class="flex justify-end gap-2.5">
            <button
              type="button"
              class="ghost h-11 rounded-full border border-line px-[18px] font-semibold"
              @click="emit('close')"
            >
              Cancel
            </button>
            <button
              type="submit"
              :disabled="saving || !name.trim()"
              class="press flex h-11 items-center gap-2 rounded-full bg-accent px-5 font-semibold text-on-accent disabled:opacity-60"
            >
              <span v-if="saving" class="spinner" aria-hidden="true" />
              {{ saving ? 'Creating…' : 'Create board' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </Teleport>
</template>
