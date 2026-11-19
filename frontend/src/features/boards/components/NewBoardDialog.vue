<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'
import { useRouter } from 'vue-router'

import type { BoardTemplate } from '../api'
import BoardDetailsFields from './BoardDetailsFields.vue'
import TemplateGallery from './TemplateGallery.vue'
import { boardColors } from '../options'
import { errorText, useBoardsStore } from '../store'

const emit = defineEmits<{ close: [] }>()

const store = useBoardsStore()
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
      class="fade-in fixed inset-0 z-40 flex items-center justify-center overflow-y-auto bg-ink/40 p-4"
      @click.self="emit('close')"
    >
      <div
        ref="panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="new-board-title"
        class="fade-up my-auto flex max-h-full w-full max-w-[600px] flex-col gap-[18px] overflow-y-auto rounded-[24px] bg-panel p-7"
      >
        <h2 id="new-board-title" class="text-xl font-bold">New board</h2>
        <form class="flex flex-col gap-4" @submit.prevent="submit">
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
          <p v-if="error" role="alert" class="text-sm text-red-600">{{ error }}</p>
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
              class="press flex h-11 items-center gap-2 rounded-full bg-accent px-5 font-semibold text-white disabled:opacity-60"
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
