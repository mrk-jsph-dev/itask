<script setup lang="ts">
import { ref } from 'vue'

import { aiApi } from '../api'
import { useAiStore } from '../store'
import AiButton from './AiButton.vue'
import AiPanel from './AiPanel.vue'

const props = defineProps<{ boardId: string; cardId: string; canPost: boolean; saving?: boolean }>()
const emit = defineEmits<{ post: [text: string] }>()

const ai = useAiStore()
const summary = ref<string | null>(null)

async function generate() {
  const result = await ai.run('summary', () => aiApi.summary(props.boardId, props.cardId))
  if (result) summary.value = result.summary
}

defineExpose({ discard: () => (summary.value = null) })
</script>

<template>
  <AiButton v-if="summary === null" label="Summarize" action="summary" compact @click="generate" />
  <AiPanel v-else title="Summary">
    <textarea
      v-model="summary"
      rows="4"
      maxlength="1500"
      aria-label="Summary"
      class="w-full resize-y rounded-[12px] border border-line bg-panel p-3 text-sm leading-relaxed"
    />
    <template #actions>
      <button
        v-if="canPost"
        type="button"
        class="press flex h-9 items-center gap-2 rounded-full bg-accent px-4 text-sm font-semibold text-on-accent disabled:opacity-60"
        :disabled="saving || !summary.trim()"
        @click="emit('post', summary.trim())"
      >
        <span v-if="saving" class="spinner" aria-hidden="true" />
        Post as comment
      </button>
      <button
        type="button"
        class="ghost h-9 rounded-full border border-line bg-panel px-4 text-sm font-semibold"
        @click="summary = null"
      >
        Discard
      </button>
    </template>
  </AiPanel>
</template>
