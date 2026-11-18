<script setup lang="ts">
import { onMounted, ref } from 'vue'

import { boardsApi, type BoardSummary } from '../api'

const props = defineProps<{ boardId: string }>()
const board = ref<BoardSummary | null>(null)

onMounted(async () => {
  board.value = await boardsApi.get(props.boardId)
})
</script>

<template>
  <section v-if="board" class="fade-in flex flex-col gap-2">
    <h1 class="text-2xl font-bold tracking-tight">{{ board.emoji }} {{ board.name }}</h1>
    <p class="text-sm text-muted">
      Your role: {{ board.role }}. Lists and cards arrive in the Boards PR.
    </p>
  </section>
</template>
