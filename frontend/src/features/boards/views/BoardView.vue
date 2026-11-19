<script setup lang="ts">
import { computed, nextTick, ref, toRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { VueDraggable, type SortableEvent } from 'vue-draggable-plus'

import { useAuthStore } from '@/features/auth/store'
import PanelHeader from '@/shared/ui/PanelHeader.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import type { BoardList, Card, CardPatch, ListInput } from '../api'
import BoardCalendarView from '../components/BoardCalendarView.vue'
import BoardListView from '../components/BoardListView.vue'
import BoardSettingsSheet from '../components/BoardSettingsSheet.vue'
import CardSheet from '../components/CardSheet.vue'
import BoardToolbar from '../components/BoardToolbar.vue'
import ListColumn from '../components/ListColumn.vue'
import NewListColumn from '../components/NewListColumn.vue'
import { useCardsStore } from '../cardsStore'
import { useListsStore } from '../listsStore'
import { errorText, useBoardsStore } from '../store'
import { activeCount, compareBy, emptyFilter, matches, type BoardState } from '../filters'
import { useBoardState } from '../useBoardState'
import { useViewsStore } from '../viewsStore'

/** `cardId` comes from `/boards/:boardId/cards/:cardId` and opens the card sheet. */
const props = defineProps<{ boardId: string; cardId?: string }>()
const route = useRoute()
const router = useRouter()
const store = useBoardsStore()
const lists = useListsStore()
const cards = useCardsStore()
const views = useViewsStore()
const auth = useAuthStore()
const { state, view, set: setState } = useBoardState(toRef(props, 'boardId'))

/** Column components by list id, so "+ New task" can open the first list's form. */
const columns = new Map<string, InstanceType<typeof ListColumn>>()
function columnRef(listId: string, el: unknown) {
  if (el) columns.set(listId, el as InstanceType<typeof ListColumn>)
  else columns.delete(listId)
}
async function newTask() {
  if (view.value !== 'board') {
    setState({ view: 'board' })
    await nextTick()
  }
  const first = ordered.value[0]
  if (first) columns.get(first.id)?.addCard()
}

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

// Opening and closing a card keeps the view, filter and sort in the query.
function openCard(cardId: string) {
  void router.push({ name: 'card', params: { boardId: props.boardId, cardId }, query: route.query })
}

function closeCard() {
  void router.push({ name: 'board', params: { boardId: props.boardId }, query: route.query })
}

// A link to a card that isn't on the board (archived, moved away, or never existed).
watch(openCardId, (id) => {
  if (id && !cards.find(id)) {
    toast('That card is not on this board.', 'error')
    void router.replace({ name: 'board', params: { boardId: props.boardId }, query: route.query })
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

/** Board view: cards can be dragged only when every card shows in board order. */
const reorderable = computed(
  () => activeCount(state.value.filter) === 0 && state.value.sort === 'position',
)

/** A list's cards as the Board view shows them (filtered and sorted). */
function shownCards(list: BoardList): Card[] {
  if (reorderable.value) return list.cards
  const now = new Date()
  return list.cards
    .filter((c) => matches(c, state.value.filter, now))
    .sort(compareBy(state.value.sort))
}

function changeState(change: Partial<BoardState>) {
  setState(change)
}

/** Days the Calendar shows, reported by the calendar itself. */
const calendarRange = ref<{ from: Date; to: Date } | null>(null)

function loadView() {
  if (notFound.value || view.value === 'board') return
  if (view.value === 'calendar' && !calendarRange.value) return
  void views.load(
    props.boardId,
    state.value.filter,
    state.value.sort,
    view.value === 'calendar' ? calendarRange.value! : undefined,
  )
}

watch(
  () => [
    view.value,
    JSON.stringify(state.value.filter),
    state.value.sort,
    calendarRange.value,
  ],
  loadView,
)

// The card sheet edits the board's lists; reload the view's own copy once it closes.
watch(
  () => props.cardId,
  (id, old) => {
    if (old && !id) loadView()
  },
)

function onCalendarRange(range: { from: Date; to: Date }) {
  calendarRange.value = range
}

/** Matching tasks without a due date, for the Calendar's note. */
const undatedCount = computed(() => {
  const now = new Date()
  return (lists.lists ?? [])
    .flatMap((l) => l.cards)
    .filter((c) => !c.dueAt && matches(c, state.value.filter, now)).length
})

function showUndated() {
  setState({ view: 'list', filter: { ...state.value.filter, due: ['none'] } })
}

function updateCard(cardId: string, patch: CardPatch) {
  void views.update(cardId, patch)
}

const listNames = computed(() =>
  ordered.value.map((l) => ({ id: l.id, name: l.name, emoji: l.emoji })),
)

watch(
  () => props.boardId,
  async (id) => {
    views.reset()
    calendarRange.value = null
    notFound.value = false
    settingsOpen.value = false
    try {
      await Promise.all([store.open(id), lists.load(id), cards.loadLabels(id)])
      loadView()
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
  <section v-if="notFound" class="fade-in flex flex-col items-start gap-3 p-5 md:p-7">
    <h1 class="text-2xl font-bold tracking-tight">Board not found</h1>
    <p class="text-sm text-muted">It may have been deleted, or you're not a member.</p>
    <RouterLink to="/" class="text-sm font-semibold">Back to Home</RouterLink>
  </section>

  <section v-else-if="store.current?.id === boardId" class="fade-in flex flex-col">
    <PanelHeader :user="auth.user">
      <template #title>
        <h1 class="min-w-0 text-[22px] font-semibold break-words">{{ store.current.name }}</h1>
      </template>
      <template #chip>
        <span
          class="rounded-full bg-[#F3F4F6] px-2.5 py-1 text-xs font-semibold text-[#374151]"
          title="Only board members can see this board"
          >Private</span
        >
        <span
          v-if="store.current.role !== 'Owner'"
          class="rounded-full bg-hover px-2.5 py-1 text-xs font-semibold text-muted"
          >{{ store.current.role }}</span
        >
      </template>
      <template #actions>
        <button
          v-if="!store.current.archivedAt"
          type="button"
          class="ghost flex h-[42px] w-[42px] items-center justify-center rounded-full text-lg"
          :aria-pressed="store.current.isStarred"
          :aria-label="store.current.isStarred ? 'Unstar board' : 'Star board'"
          :title="store.current.isStarred ? 'Unstar board' : 'Star board'"
          @click="store.toggleStar(store.current.id, store.current.isStarred)"
        >
          <span
            :class="store.current.isStarred ? 'pop text-amber-500' : 'text-muted'"
            aria-hidden="true"
            >{{ store.current.isStarred ? '★' : '☆' }}</span
          >
        </button>
        <RouterLink
          :to="{ name: 'board-activity', params: { boardId } }"
          class="ghost flex h-[42px] w-[42px] items-center justify-center rounded-full text-ink no-underline"
          aria-label="Board activity"
          title="Board activity"
        >
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path d="M13 2L3 14h9l-1 8 10-12h-9z" />
          </svg>
        </RouterLink>
        <button
          type="button"
          class="ghost flex h-[42px] items-center gap-1.5 rounded-full px-3 text-sm font-semibold"
          :aria-label="store.isOwner ? 'Board settings' : 'Board members'"
          :title="store.isOwner ? 'Settings' : 'Members'"
          @click="settingsOpen = true"
        >
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <circle cx="12" cy="12" r="3" />
            <path
              d="M19.4 15a1.7 1.7 0 00.3 1.8l.1.1a2 2 0 11-2.8 2.8l-.1-.1a1.7 1.7 0 00-1.8-.3 1.7 1.7 0 00-1 1.5V21a2 2 0 11-4 0v-.1a1.7 1.7 0 00-1.1-1.5 1.7 1.7 0 00-1.8.3l-.1.1a2 2 0 11-2.8-2.8l.1-.1a1.7 1.7 0 00.3-1.8 1.7 1.7 0 00-1.5-1H3a2 2 0 110-4h.1a1.7 1.7 0 001.5-1.1 1.7 1.7 0 00-.3-1.8l-.1-.1a2 2 0 112.8-2.8l.1.1a1.7 1.7 0 001.8.3H9a1.7 1.7 0 001-1.5V3a2 2 0 114 0v.1a1.7 1.7 0 001 1.5 1.7 1.7 0 001.8-.3l.1-.1a2 2 0 112.8 2.8l-.1.1a1.7 1.7 0 00-.3 1.8V9a1.7 1.7 0 001.5 1H21a2 2 0 110 4h-.1a1.7 1.7 0 00-1.5 1z"
            />
          </svg>
          <span class="hidden sm:inline">{{ store.isOwner ? 'Settings' : 'Members' }}</span>
        </button>
        <button
          v-if="editable && ordered.length"
          type="button"
          class="press h-[42px] rounded-full bg-accent px-[18px] text-[15px] font-semibold text-white"
          @click="newTask"
        >
          + New task
        </button>
      </template>
    </PanelHeader>
    <BoardToolbar
      :state="state"
      :labels="cards.labels ?? []"
      :members="store.members"
      @change="changeState"
    />

    <div class="flex flex-col gap-5 px-5 pt-5 pb-7 md:px-7">
      <div
        v-if="store.current.archivedAt"
        role="status"
        class="rounded-2xl bg-amber-50 px-4 py-3 text-sm text-amber-900"
      >
        🗄 This board is archived.
        <template v-if="store.isOwner">Restore it from Settings to work on it again.</template>
      </div>
      <p v-if="store.current.description" class="text-sm text-muted">
        {{ store.current.description }}
      </p>

      <BoardListView
        v-if="view === 'list'"
        :cards="views.cards"
        :truncated="views.truncated"
        :group="state.group"
        :lists="listNames"
        :labels="cards.labels ?? []"
        :members="store.members"
        :editable="editable"
        @open="openCard"
        @update="updateCard"
      />
      <BoardCalendarView
        v-else-if="view === 'calendar'"
        :cards="views.cards"
        :month="state.month"
        :editable="editable"
        :undated-count="undatedCount"
        @update:month="(month) => setState({ month })"
        @range="onCalendarRange"
        @open="openCard"
        @move="(cardId, dueAt) => updateCard(cardId, { dueAt })"
        @show-undated="showUndated"
      />
      <template v-else>
        <p
          v-if="editable && !reorderable && lists.lists?.length"
          role="status"
          class="flex flex-wrap items-center gap-2 text-sm text-muted"
        >
          Some tasks are hidden or sorted, so dragging is off.
          <button
            type="button"
            class="font-semibold text-accent"
            @click="setState({ filter: emptyFilter(), sort: 'position' })"
          >
            Show board order
          </button>
        </p>
        <div
          v-if="lists.lists === null"
          class="board-grid"
          aria-busy="true"
          aria-label="Loading lists"
        >
          <div v-for="i in 4" :key="i" class="flex flex-col gap-2.5">
            <SkeletonBlock height="28px" width="60%" />
            <div
              class="flex flex-col gap-2 rounded-[18px] border-[1.5px] border-dashed border-line p-2"
            >
              <SkeletonBlock height="96px" />
              <SkeletonBlock height="96px" />
            </div>
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
            :style="{ '--i': Math.min(i, 8) }"
          >
            <ListColumn
              :ref="(el) => columnRef(list.id, el)"
              :cards="shownCards(list)"
              :list="list"
              :editable="editable"
              :reorderable="reorderable"
              @update:cards="(value) => (list.cards = value)"
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
      </template>
    </div>
    <BoardSettingsSheet v-if="settingsOpen" @close="settingsOpen = false" />
    <CardSheet
      v-if="openCardId"
      :card-id="openCardId"
      :editable="editable"
      :can-comment="!store.current.archivedAt"
      @close="closeCard"
    />
  </section>

  <section v-else class="flex flex-col gap-3 p-5 md:p-7" aria-busy="true">
    <SkeletonBlock height="32px" width="40%" />
    <SkeletonBlock height="16px" width="25%" />
  </section>
</template>

<style scoped>
.board-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(230px, 100%), 1fr));
  gap: 16px;
  align-items: start;
}
</style>
