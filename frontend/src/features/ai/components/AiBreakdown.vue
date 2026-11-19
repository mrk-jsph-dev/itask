<script setup lang="ts">
import { ref } from 'vue'

import { aiApi } from '../api'
import { useAiStore } from '../store'
import AiButton from './AiButton.vue'
import AiPanel from './AiPanel.vue'

const props = defineProps<{ boardId: string; cardId: string; saving?: boolean }>()
const emit = defineEmits<{ accept: [items: string[]] }>()

const ai = useAiStore()
/** Editable draft rows; unticked rows are left out on Accept. */
const draft = ref<{ text: string; keep: boolean }[] | null>(null)

async function generate() {
  const result = await ai.run('breakdown', () => aiApi.breakdown(props.boardId, props.cardId))
  if (result) draft.value = result.items.map((text) => ({ text, keep: true }))
}

function accept() {
  const items = (draft.value ?? []).filter((i) => i.keep && i.text.trim()).map((i) => i.text.trim())
  if (items.length) emit('accept', items)
}

defineExpose({ discard: () => (draft.value = null) })
</script>

<template>
  <AiButton v-if="!draft" label="Break down" action="breakdown" compact @click="generate" />
  <AiPanel v-else title="Suggested checklist">
    <ul class="flex flex-col gap-1.5">
      <li v-for="(item, i) in draft" :key="i" class="flex items-center gap-2" :style="{ '--i': i }">
        <input
          v-model="item.keep"
          type="checkbox"
          class="h-4 w-4 accent-accent"
          :aria-label="`Keep step ${i + 1}`"
        />
        <input
          v-model="item.text"
          class="h-9 min-w-0 flex-1 rounded-[10px] border border-line bg-panel px-2.5 text-sm"
          :aria-label="`Step ${i + 1}`"
          maxlength="300"
        />
      </li>
    </ul>
    <template #actions>
      <button
        type="button"
        class="press flex h-9 items-center gap-2 rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-60"
        :disabled="saving || !draft.some((i) => i.keep && i.text.trim())"
        @click="accept"
      >
        <span v-if="saving" class="spinner" aria-hidden="true" />
        Add to checklist
      </button>
      <button
        type="button"
        class="ghost h-9 rounded-full border border-line bg-panel px-4 text-sm font-semibold"
        @click="draft = null"
      >
        Discard
      </button>
    </template>
  </AiPanel>
</template>
