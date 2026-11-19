<script setup lang="ts">
import { onMounted, ref } from 'vue'

import SideSheet from '@/shared/ui/SideSheet.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { aiApi, type BoardDigest } from '../api'
import { useAiStore } from '../store'
import AiButton from './AiButton.vue'

const props = defineProps<{ boardId: string; boardName: string }>()
const emit = defineEmits<{ close: [] }>()

const ai = useAiStore()
const digest = ref<BoardDigest | null>(null)

async function generate() {
  const result = await ai.run('digest', () => aiApi.digest(props.boardId))
  if (result) digest.value = result
}

onMounted(generate)
</script>

<template>
  <SideSheet :title="`✨ Daily digest · ${boardName}`" @close="emit('close')">
    <div class="flex flex-col gap-4 p-5">
      <div
        v-if="ai.busy === 'digest'"
        class="flex flex-col gap-2"
        aria-busy="true"
        aria-label="Writing digest"
      >
        <SkeletonBlock height="18px" width="40%" />
        <SkeletonBlock height="96px" />
      </div>
      <template v-else-if="digest">
        <ul class="flex flex-wrap gap-2 text-xs font-semibold" aria-label="Digest counts">
          <li class="rounded-full bg-red-50 px-2.5 py-1 text-red-700">⏰ {{ digest.due }} due</li>
          <li class="rounded-full bg-amber-50 px-2.5 py-1 text-amber-800">
            🧱 {{ digest.stuck }} stuck
          </li>
          <li class="rounded-full bg-emerald-50 px-2.5 py-1 text-emerald-800">
            ✅ {{ digest.done }} done
          </li>
        </ul>
        <p
          class="fade-up rounded-[16px] border border-[#DCE6FE] bg-[#F5F8FF] p-4 text-sm leading-relaxed whitespace-pre-line"
          data-testid="ai-digest"
        >
          {{ digest.digest }}
        </p>
      </template>
      <p v-else class="text-sm text-muted">No digest yet.</p>
      <div class="flex flex-wrap items-center gap-2">
        <AiButton
          :label="digest ? 'Write again' : 'Write digest'"
          action="digest"
          compact
          @click="generate"
        />
        <p class="ml-auto text-xs text-muted">
          {{ ai.remaining }} of {{ ai.limit }} AI requests left today
        </p>
      </div>
    </div>
  </SideSheet>
</template>
