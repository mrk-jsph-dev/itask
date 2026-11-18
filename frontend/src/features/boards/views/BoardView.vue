<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { VueDraggable, type SortableEvent } from 'vue-draggable-plus'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import type { BoardList, ListInput } from '../api'
import BoardSettingsSheet from '../components/BoardSettingsSheet.vue'
import CardSheet from '../components/CardSheet.vue'
import BoardToolbar from '../components/BoardToolbar.vue'
import ListColumn from '../components/ListColumn.vue'
import NewListColumn from '../components/NewListColumn.vue'
import { useCardsStore } from '../cardsStore'
import { useListsStore } from '../listsStore'
import { errorText, useBoardsStore } from '../store'

/** `cardId` comes from `/boards/:boardId/cards/:cardId` and opens the card sheet. */
const props = defineProps<{ boardId: string; cardId?: string }>()
const router = useRouter()
const store = useBoardsStore()
const lists = useListsStore()
const cards = useCardsStore()

const notFound = ref(false)
const settingsOpen = ref(false)
/** The list that just dropped, for a brief glow. */
const settledId = ref<string | null>(null)
/** The card that just dropped, for a brief glow. */
const settledCardId = ref<string | null>(null)
/** Card open in the detail sheet, once the board's cards are loaded. */
const openCardId = computed(() =>
  props.cardId && lists.boardId === props.boardId && lists.lists ? props.cardId : null,
)

function openCard(cardId: string) {
  void router.push({ name: 'card', params: { boardId: props.boardId, cardId } })
}

function closeCard() {
  void router.push({ name: 'board', params: { boardId: props.boardId } })
}

// A link to a card that isn't on the board (archived, moved away, or never existed).
watch(openCardId, (id) => {
  if (id && !cards.find(id)) {
    toast('That card is not on this board.', 'error')
    void router.replace({ name: 'board', params: { boardId: props.boardId } })
  }
})

/** Editors and owners change lists, unless the board is archived. */
const editable = computed(
  () => !!store.current && store.current.role !== 'Viewer' && !store.current.archivedAt,
)

const ordered = computed<BoardList[]>({
  get: () => lists.lists ?? [],
  set: (value) => (lists.lists = value),
})

watch(
  () => props.boardId,
  async (id) => {
    notFound.value = false
    settingsOpen.value = false
    try {
      await Promise.all([store.open(id), lists.load(id), cards.loadLabels(id)])
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) notFound.value = true
      else toast(errorText(e, "Couldn't load the board."), 'error')
    }
  },
  { immediate: true },
)

function onDrop(event: SortableEvent) {
  const from = event.oldDraggableIndex
  const to = event.newDraggableIndex
  if (from === undefined || to === undefined || from === to) return
  settledId.value = ordered.value[to]?.id ?? null
  setTimeout(() => (settledId.value = null), 400)
  void lists.commitMove(from, to)
}

const listTargets = computed(() =>
  ordered.value.map((l) => ({ id: l.id, name: l.name, emoji: l.emoji })),
)

function settleCard(cardId: string) {
  settledCardId.value = cardId
  setTimeout(() => (settledCardId.value = null), 400)
}

function onCardDrop(cardId: string) {
  settleCard(cardId)
  void cards.commitDrag(cardId)
}

function moveCard(cardId: string, listId: string, index: number) {
  settleCard(cardId)
  void cards.move(cardId, listId, index)
}

async function createCard(listId: string, title: string, done: (ok: boolean) => void) {
  try {
    await cards.create(listId, title)
    done(true)
  } catch (e) {
    toast(errorText(e, "Couldn't add the card."), 'error')
    done(false)
  }
}

function moveBy(index: number, delta: -1 | 1) {
  void lists.move(index, index + delta)
}

async function create(input: ListInput, done: (ok: boolean) => void) {
  try {
    await lists.create(input)
    done(true)
  } catch (e) {
    toast(errorText(e, "Couldn't add the list."), 'error')
    done(false)
  }
}

async function rename(listId: string, input: ListInput, done: (ok: boolean) => void) {
  try {
    await lists.rename(listId, input)
    done(true)
  } catch (e) {
    toast(errorText(e, "Couldn't rename the list."), 'error')
    done(false)
  }
}

function onArchivedToggle(event: Event) {
  if ((event.target as HTMLDetailsElement).open && !lists.archived) {
    lists
      .loadArchived()
      .catch((e: unknown) => toast(errorText(e, "Couldn't load archived lists."), 'error'))
  }
}
</script>

<template>
  <section v-if="notFound" class="fade-in flex flex-col items-start gap-3">
    <h1 class="text-2xl font-bold tracking-tight">Board not found</h1>
    <p class="text-sm text-muted">It may have been deleted, or you're not a member.</p>
    <RouterLink to="/" class="text-sm font-semibold">Back to Home</RouterLink>
  </section>

  <section v-else-if="store.current?.id === boardId" class="fade-in flex flex-col gap-6">
    <div
      v-if="store.current.archivedAt"
      role="status"
      class="rounded-2xl bg-amber-50 px-4 py-3 text-sm text-amber-900"
    >
      🗄 This board is archived.
      <template v-if="store.isOwner">Restore it from Settings to work on it again.</template>
    </div>
    <header class="flex flex-wrap items-start justify-between gap-4">
      <div class="flex min-w-0 items-start gap-3">
        <span
          class="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl text-2xl"
          :style="{ backgroundColor: `${store.current.color}1A` }"
          aria-hidden="true"
          >{{ store.current.emoji ?? '📋' }}</span
        >
        <div class="flex min-w-0 flex-col gap-1">
          <h1 class="text-2xl font-bold tracking-tight">{{ store.current.name }}</h1>
          <p v-if="store.current.description" class="text-sm text-muted">
            {{ store.current.description }}
          </p>
          <p class="text-xs text-muted">
            {{ store.current.role }} · {{ store.current.memberCount }}
            {{ store.current.memberCount === 1 ? 'member' : 'members' }}
          </p>
        </div>
      </div>
      <div class="flex items-center gap-2">
        <button
          v-if="!store.current.archivedAt"
          type="button"
          class="ghost flex h-10 w-10 items-center justify-center rounded-full border border-line text-lg"
          :aria-pressed="store.current.isStarred"
          :aria-label="store.current.isStarred ? 'Unstar board' : 'Star board'"
          @click="store.toggleStar(store.current.id, store.current.isStarred)"
        >
          <span
            :class="store.current.isStarred ? 'pop text-amber-500' : 'text-muted'"
            aria-hidden="true"
            >{{ store.current.isStarred ? '★' : '☆' }}</span
          >
        </button>
        <button
          type="button"
          class="ghost h-10 rounded-full border border-line px-4 text-sm font-semibold"
          @click="settingsOpen = true"
        >
          ⚙ {{ store.isOwner ? 'Settings' : 'Members' }}
        </button>
      </div>
    </header>
    <BoardToolbar :members="store.members" />

    <div v-if="lists.lists === null" class="board-grid" aria-busy="true" aria-label="Loading lists">
      <div v-for="i in 4" :key="i" class="flex flex-col gap-2.5">
        <SkeletonBlock height="28px" width="60%" />
        <SkeletonBlock height="96px" />
      </div>
    </div>
    <VueDraggable
      v-else
      v-model="ordered"
      class="board-grid"
      :disabled="!editable"
      draggable="[data-list]"
      handle=".list-handle"
      ghost-class="drag-ghost"
      drag-class="dragging"
      :animation="200"
      :force-fallback="true"
      @end="onDrop"
    >
      <div
        v-for="(list, i) in ordered"
        :key="list.id"
        data-list
        class="fade-up rounded-[18px]"
        :class="{ settle: settledId === list.id }"
        :style="{ '--i': i }"
      >
        <ListColumn
          v-model:cards="list.cards"
          :list="list"
          :editable="editable"
          :can-move-left="i > 0"
          :can-move-right="i < ordered.length - 1"
          :all-lists="listTargets"
          :settled-card-id="settledCardId"
          @rename="(input, done) => rename(list.id, input, done)"
          @move="(delta) => moveBy(i, delta)"
          @archive="lists.archive(list.id)"
          @create-card="(title, done) => createCard(list.id, title, done)"
          @open-card="openCard"
          @move-card="moveCard"
          @toggle-complete="cards.toggleComplete"
          @archive-card="cards.archive"
          @drag-start="cards.beginDrag"
          @drop-card="onCardDrop"
        />
      </div>
      <NewListColumn v-if="editable" @create="create" />
    </VueDraggable>

    <p v-if="lists.lists?.length === 0 && !editable" class="text-sm text-muted">
      🌱 This board has no lists yet.
    </p>

    <details v-if="editable" class="text-sm" @toggle="onArchivedToggle">
      <summary class="cursor-pointer font-semibold text-muted">🗄 Archived lists</summary>
      <p v-if="lists.archived === null" class="mt-2 text-muted">Loading…</p>
      <p v-else-if="lists.archived.length === 0" class="mt-2 text-muted">Nothing archived.</p>
      <ul v-else class="mt-2 flex flex-col gap-1" aria-label="Archived lists">
        <li
          v-for="list in lists.archived"
          :key="list.id"
          class="flex items-center gap-3 rounded-xl px-3 py-2 hover:bg-hover"
        >
          <span aria-hidden="true">{{ list.emoji }}</span>
          <span class="min-w-0 flex-1 truncate">{{ list.name }}</span>
          <button
            type="button"
            class="ghost rounded-full border border-line px-3 py-1 font-semibold"
            :aria-label="`Restore ${list.name}`"
            @click="lists.restore(list.id)"
          >
            Restore
          </button>
        </li>
      </ul>
    </details>
    <BoardSettingsSheet v-if="settingsOpen" @close="settingsOpen = false" />
    <CardSheet
      v-if="openCardId"
      :card-id="openCardId"
      :editable="editable"
      :can-comment="!store.current.archivedAt"
      @close="closeCard"
    />
  </section>

  <section v-else class="flex flex-col gap-3" aria-busy="true">
    <SkeletonBlock height="32px" width="40%" />
    <SkeletonBlock height="16px" width="25%" />
  </section>
</template>

<style scoped>
.board-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 16px;
  align-items: start;
}
</style>
