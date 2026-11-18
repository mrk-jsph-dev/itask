<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { toast } from '@/shared/ui/toast'
import { useAuthStore } from '@/features/auth/store'
import BoardTile from '../components/BoardTile.vue'
import NewBoardSheet from '../components/NewBoardSheet.vue'
import { greeting } from '../options'
import { errorText, useBoardsStore } from '../store'

const store = useBoardsStore()
const auth = useAuthStore()

const creating = ref(false)
const hello = computed(() => {
  const first = auth.user?.displayName.split(' ')[0]
  return first ? `${greeting()}, ${first} 👋` : `${greeting()} 👋`
})

async function load(archived: boolean) {
  try {
    await store.load(archived)
  } catch (e) {
    store.boards = []
    toast(errorText(e, "Couldn't load your boards."), 'error')
  }
}

onMounted(() => load(false))
</script>

<template>
  <section class="flex flex-col gap-8">
    <header class="flex flex-wrap items-end justify-between gap-4">
      <div class="flex flex-col gap-1">
        <h1 class="text-2xl font-bold tracking-tight">{{ hello }}</h1>
        <p class="text-sm text-muted">
          {{
            store.showArchived
              ? 'Archived boards stay here until you restore or delete them.'
              : 'Pick up where you left off.'
          }}
        </p>
      </div>
      <button
        type="button"
        class="press h-11 rounded-full bg-accent px-5 text-sm font-semibold text-white"
        @click="creating = true"
      >
        + New board
      </button>
    </header>

    <section
      v-if="!store.showArchived && store.starred.length"
      class="flex flex-col gap-3"
      aria-labelledby="starred-heading"
    >
      <h2 id="starred-heading" class="text-base font-semibold">⭐ Starred</h2>
      <div class="grid grid-cols-[repeat(auto-fill,minmax(230px,1fr))] gap-4">
        <BoardTile
          v-for="(board, i) in store.starred"
          :key="board.id"
          :board="board"
          :index="i"
          @star="store.toggleStar(board.id, board.isStarred)"
        />
      </div>
    </section>

    <section class="flex flex-col gap-3" aria-labelledby="boards-heading">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 id="boards-heading" class="text-base font-semibold">
          {{ store.showArchived ? '🗄 Archived boards' : '📋 Your boards' }}
        </h2>
        <div
          class="flex items-center gap-1 rounded-full bg-shell p-1 text-sm font-medium"
          role="group"
          aria-label="Board filter"
        >
          <button
            type="button"
            class="rounded-full px-4 py-1.5"
            :class="store.showArchived ? 'ghost text-muted' : 'bg-ink text-white'"
            :aria-pressed="!store.showArchived"
            @click="load(false)"
          >
            Active
          </button>
          <button
            type="button"
            class="rounded-full px-4 py-1.5"
            :class="store.showArchived ? 'bg-ink text-white' : 'ghost text-muted'"
            :aria-pressed="store.showArchived"
            @click="load(true)"
          >
            Archived
          </button>
        </div>
      </div>
      <div class="grid grid-cols-[repeat(auto-fill,minmax(230px,1fr))] gap-4">
        <template v-if="store.boards === null">
          <SkeletonBlock v-for="n in 4" :key="n" height="140px" />
        </template>
        <template v-else>
          <BoardTile
            v-for="(board, i) in store.boards"
            :key="board.id"
            :board="board"
            :index="i"
            @star="store.toggleStar(board.id, board.isStarred)"
          />
          <button
            v-if="!store.showArchived"
            type="button"
            class="lift fade-up flex min-h-[140px] items-center justify-center rounded-2xl border border-dashed border-line text-sm font-semibold text-muted"
            :style="{ '--i': store.boards.length }"
            @click="creating = true"
          >
            + New board
          </button>
        </template>
      </div>
      <p v-if="store.showArchived && store.boards?.length === 0" class="text-sm text-muted">
        No archived boards.
      </p>
    </section>

    <NewBoardSheet v-if="creating" @close="creating = false" />
  </section>
</template>
