<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'

import PanelHeader from '@/shared/ui/PanelHeader.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { toast } from '@/shared/ui/toast'
import { useAuthStore } from '@/features/auth/store'
import { boardsApi, type CardPriority, type DueSoon } from '../api'
import BoardTile from '../components/BoardTile.vue'
import { priorityStyle } from '../cardOptions'
import { dueDayLabel, dueSummary } from '../home'
import { greeting } from '../options'
import { errorText, useBoardsStore } from '../store'

const store = useBoardsStore()
const auth = useAuthStore()

const hello = computed(() => {
  const first = auth.user?.displayName.split(' ')[0]
  return first ? `${greeting()}, ${first} 👋` : `${greeting()} 👋`
})

/** Null while loading; stays null if the request fails (Home still works without it). */
const due = ref<DueSoon | null>(null)
const dueFailed = ref(false)
const now = new Date()
const pill = (p: CardPriority) =>
  p === 'None' ? null : { color: priorityStyle[p].color, backgroundColor: priorityStyle[p].bg }

const ordered = computed(() =>
  store.boards ? [...store.starred, ...store.boards.filter((b) => !b.isStarred)] : null,
)

async function load(archived: boolean) {
  try {
    await store.load(archived)
  } catch (e) {
    store.boards = []
    toast(errorText(e, "Couldn't load your boards."), 'error')
  }
}

onMounted(() => {
  void load(false)
  boardsApi
    .dueSoon()
    .then((d) => (due.value = d))
    .catch(() => (dueFailed.value = true))
})
</script>

<template>
  <PanelHeader title="Home" :user="auth.user">
    <template #actions>
      <button
        type="button"
        class="press h-[42px] rounded-full bg-accent px-[18px] text-[15px] font-semibold text-white"
        @click="store.creating = true"
      >
        + New board
      </button>
    </template>
  </PanelHeader>

  <div class="flex flex-col gap-7 p-5 md:p-7">
    <div class="flex flex-col gap-1.5">
      <h2 class="text-[26px] font-bold tracking-[-0.02em]">{{ hello }}</h2>
      <p class="text-[15px] text-muted">
        <template v-if="due">{{ dueSummary(due.dueThisWeek, due.overdue) }}</template>
        <template v-else>Pick up where you left off.</template>
      </p>
    </div>

    <div class="flex flex-wrap items-start gap-6">
      <section
        v-if="!dueFailed"
        class="flex min-w-0 flex-[1_1_380px] flex-col gap-2.5 rounded-[20px] border-[1.5px] border-dashed border-line p-3.5"
        aria-labelledby="due-heading"
      >
        <h3 id="due-heading" class="px-1.5 py-1 text-base font-semibold">
          ⏰ Due soon
          <span v-if="due" class="font-medium text-muted">{{ due.dueThisWeek }}</span>
        </h3>
        <template v-if="due === null">
          <SkeletonBlock v-for="n in 3" :key="n" height="58px" />
        </template>
        <p v-else-if="due.items.length === 0" class="px-1.5 pb-2 text-sm text-muted">
          🌱 Nothing due in the next 7 days.
        </p>
        <ol v-else class="flex flex-col gap-2.5">
          <li v-for="(item, i) in due.items" :key="item.id" class="fade-up" :style="{ '--i': i }">
            <RouterLink
              :to="{ name: 'card', params: { boardId: item.boardId, cardId: item.id } }"
              class="lift flex items-center gap-3 rounded-[14px] border border-line-soft bg-panel px-3.5 py-3 text-ink no-underline"
            >
              <span class="text-lg" aria-hidden="true">{{
                item.emoji ?? item.boardEmoji ?? '📌'
              }}</span>
              <span class="flex min-w-0 flex-1 flex-col gap-0.5">
                <span class="truncate text-sm font-semibold">{{ item.title }}</span>
                <span class="truncate text-[13px] text-muted">{{ item.boardName }}</span>
              </span>
              <span
                v-if="pill(item.priority)"
                class="rounded-full px-2.5 py-1 text-xs font-semibold"
                :style="pill(item.priority)!"
                >{{ item.priority }}</span
              >
              <time
                :datetime="item.dueAt"
                class="min-w-14 text-right text-[13px] font-medium"
                :class="new Date(item.dueAt) < now ? 'text-red-700' : 'text-[#374151]'"
                ><span v-if="new Date(item.dueAt) < now" class="sr-only">Overdue, </span
                >{{ dueDayLabel(item.dueAt, now) }}</time
              >
            </RouterLink>
          </li>
        </ol>
      </section>

      <section
        class="flex min-w-0 flex-[1_1_380px] flex-col gap-3"
        aria-labelledby="boards-heading"
      >
        <h3 id="boards-heading" class="px-1.5 py-1 text-base font-semibold">
          {{ store.showArchived ? '🗄 Archived boards' : '📋 Your boards' }}
        </h3>
        <div class="grid grid-cols-[repeat(auto-fill,minmax(200px,1fr))] gap-3">
          <template v-if="ordered === null">
            <SkeletonBlock v-for="n in 3" :key="n" height="120px" />
          </template>
          <template v-else>
            <BoardTile
              v-for="(board, i) in ordered"
              :key="board.id"
              :board="board"
              :index="i"
              @star="store.toggleStar(board.id, board.isStarred)"
            />
            <button
              v-if="!store.showArchived"
              type="button"
              class="ghost fade-up min-h-[120px] rounded-[18px] border-[1.5px] border-dashed border-line bg-panel text-sm font-semibold text-[#374151]"
              :style="{ '--i': Math.min(ordered.length, 8) }"
              @click="store.creating = true"
            >
              + New board
            </button>
          </template>
        </div>
        <p v-if="store.showArchived && store.boards?.length === 0" class="text-sm text-muted">
          No archived boards.
        </p>
        <button
          type="button"
          class="w-fit px-1.5 text-sm font-semibold text-accent-strong"
          :aria-pressed="store.showArchived"
          @click="load(!store.showArchived)"
        >
          {{ store.showArchived ? '← Back to active boards' : '🗄 Show archived boards' }}
        </button>
      </section>
    </div>
  </div>
</template>
