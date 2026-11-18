<script setup lang="ts">
import { nextTick, ref, useId, useTemplateRef } from 'vue'

defineProps<{ listName: string }>()
const emit = defineEmits<{ create: [title: string, done: (ok: boolean) => void] }>()

const open = ref(false)
const busy = ref(false)
const title = ref('')
const input = useTemplateRef('input')
const inputId = useId()

async function start() {
  open.value = true
  await nextTick()
  input.value?.focus()
}

function cancel() {
  open.value = false
  title.value = ''
}

defineExpose({ start })

/** Saves and stays open for the next card. */
function submit() {
  const trimmed = title.value.trim()
  if (!trimmed || busy.value) return
  busy.value = true
  emit('create', trimmed, (ok) => {
    busy.value = false
    if (ok) title.value = ''
    input.value?.focus()
  })
}
</script>

<template>
  <form
    v-if="open"
    class="fade-in flex flex-col gap-2"
    @submit.prevent="submit"
    @keydown.esc.stop="cancel"
  >
    <label class="sr-only" :for="inputId">Task title</label>
    <textarea
      :id="inputId"
      ref="input"
      v-model="title"
      rows="2"
      maxlength="200"
      placeholder="What needs doing?"
      class="resize-none rounded-[var(--radius-control)] border border-line bg-panel px-3 py-2 text-[15px]"
      @keydown.enter.exact.prevent="submit"
    />
    <div class="flex justify-end gap-2">
      <button
        type="button"
        class="ghost h-9 rounded-full px-4 text-sm font-semibold"
        @click="cancel"
      >
        Cancel
      </button>
      <button
        type="submit"
        class="press h-9 rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-60"
        :disabled="busy || !title.trim()"
      >
        Add task
      </button>
    </div>
  </form>
  <button
    v-else
    type="button"
    class="ghost h-11 rounded-[12px] border border-line bg-panel text-sm font-medium text-ink"
    :aria-label="`Add task to ${listName}`"
    @click="start"
  >
    + Add task
  </button>
</template>
