<script setup lang="ts">
import { ref } from 'vue'

import { aiApi } from '../api'
import { goalBoardContent, type GoalBoardContent } from '../draft'
import { useAiStore } from '../store'
import AiButton from './AiButton.vue'

defineProps<{ saving?: boolean }>()
const emit = defineEmits<{ accept: [content: GoalBoardContent] }>()

const ai = useAiStore()
const goal = ref('')
const name = ref('')
const emoji = ref<string | null>(null)
const lists = ref<string[] | null>(null)
const cards = ref<{ listIndex: number; title: string; checklist: string[] }[]>([])

async function generate() {
  if (!goal.value.trim()) return
  const draft = await ai.run('board-from-goal', () => aiApi.boardFromGoal(goal.value.trim()))
  if (!draft) return
  name.value = draft.name
  emoji.value = draft.emoji
  lists.value = [...draft.lists]
  cards.value = draft.cards.map((c) => ({
    listIndex: draft.lists.indexOf(c.list),
    title: c.title,
    checklist: c.checklist,
  }))
}

function removeList(index: number) {
  if (!lists.value) return
  lists.value = lists.value.filter((_, i) => i !== index)
  cards.value = cards.value
    .filter((c) => c.listIndex !== index)
    .map((c) => (c.listIndex > index ? { ...c, listIndex: c.listIndex - 1 } : c))
}

function accept() {
  if (!lists.value) return
  const content = goalBoardContent(
    { name: name.value, emoji: emoji.value },
    lists.value,
    cards.value,
  )
  if (content.name && content.content.lists.length) emit('accept', content)
}

const field = 'h-10 w-full rounded-[12px] border border-line bg-panel px-3 text-sm'
</script>

<template>
  <div class="flex flex-col gap-4">
    <form class="flex flex-col gap-2" @submit.prevent="generate">
      <label for="ai-goal" class="text-sm font-semibold">What do you want to achieve?</label>
      <input
        id="ai-goal"
        v-model="goal"
        :class="field"
        maxlength="300"
        placeholder="Launch my portfolio site"
        autocomplete="off"
      />
      <div class="flex flex-wrap items-center gap-2">
        <AiButton
          :label="lists ? 'Try again' : 'Plan board'"
          action="board-from-goal"
          compact
          @click="generate"
        />
        <p class="ml-auto text-xs text-muted">
          {{ ai.remaining }} of {{ ai.limit }} AI requests left today
        </p>
      </div>
    </form>

    <form
      v-if="lists"
      class="fade-up flex flex-col gap-3 rounded-[16px] border border-[#DCE6FE] bg-[#F5F8FF] p-4"
      aria-label="Board draft"
      @submit.prevent="accept"
    >
      <div class="grid grid-cols-[64px_1fr] gap-2">
        <input v-model="emoji" :class="field" class="text-center" aria-label="Board emoji" />
        <input v-model="name" :class="field" maxlength="120" aria-label="Board name" required />
      </div>
      <div
        class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-2"
        aria-label="Lists"
        role="group"
      >
        <div
          v-for="(list, i) in lists"
          :key="i"
          class="flex flex-col gap-1.5 rounded-[12px] bg-panel p-2.5"
          :style="{ '--i': i }"
        >
          <div class="flex items-center gap-1">
            <input
              v-model="lists[i]"
              class="h-8 min-w-0 flex-1 rounded-[8px] border border-line px-2 text-sm font-semibold"
              maxlength="60"
              :aria-label="`List ${i + 1} name`"
            />
            <button
              type="button"
              class="ghost h-8 w-8 shrink-0 rounded-full text-muted"
              :aria-label="`Remove list ${list}`"
              @click="removeList(i)"
            >
              ✕
            </button>
          </div>
          <ul class="flex flex-col gap-1 text-sm">
            <li
              v-for="card in cards.filter((c) => c.listIndex === i)"
              :key="card.title"
              class="flex items-start gap-1 rounded-[8px] bg-hover px-2 py-1"
            >
              <span class="min-w-0 flex-1 break-words">
                {{ card.title }}
                <span v-if="card.checklist.length" class="text-xs text-muted">
                  · {{ card.checklist.length }} steps</span
                >
              </span>
              <button
                type="button"
                class="ghost shrink-0 rounded-full px-1 text-xs text-muted"
                :aria-label="`Remove card ${card.title}`"
                @click="cards = cards.filter((c) => c !== card)"
              >
                ✕
              </button>
            </li>
          </ul>
        </div>
      </div>
      <div class="flex justify-end gap-2.5">
        <button
          type="button"
          class="ghost h-10 rounded-full border border-line bg-panel px-4 text-sm font-semibold"
          @click="lists = null"
        >
          Discard
        </button>
        <button
          type="submit"
          class="press flex h-10 items-center gap-2 rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-60"
          :disabled="saving || !name.trim() || !lists.some((l) => l.trim())"
        >
          <span v-if="saving" class="spinner" aria-hidden="true" />
          Create board
        </button>
      </div>
    </form>
  </div>
</template>
