<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'

import { ApiError } from '@/shared/api/http'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { toast } from '@/shared/ui/toast'
import { activityApi, type BoardMemberOption } from '../api'
import ActivityRow from '../components/ActivityRow.vue'
import { activityChips } from '../format'
import { useActivityStore } from '../store'

/** Without `boardId` this is the caller's own feed across boards. */
const props = defineProps<{ boardId?: string }>()

const store = useActivityStore()
const board = ref<{ name: string; emoji: string | null } | null>(null)
const members = ref<BoardMemberOption[]>([])
const missing = ref(false)

const title = computed(() =>
  props.boardId
    ? `${board.value?.emoji ?? '⚡'} ${board.value?.name ?? 'Board'} activity`
    : '⚡ Recent activity',
)

async function run(action: () => Promise<void>, fallback: string) {
  try {
    await action()
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) missing.value = true
    else toast(fallback, 'error')
  }
}

watch(
  () => props.boardId ?? null,
  async (id) => {
    missing.value = false
    board.value = null
    members.value = []
    if (id) {
      void activityApi
        .boardName(id)
        .then((b) => (board.value = b))
        .catch(() => {})
      void activityApi
        .members(id)
        .then((m) => (members.value = m))
        .catch(() => {})
    }
    await run(() => store.open(id), "Couldn't load the activity.")
  },
  { immediate: true },
)

const pick = (category: (typeof activityChips)[number]['category']) =>
  run(() => store.setCategory(category), "Couldn't load the activity.")

const pickMember = (event: Event) =>
  run(
    () => store.setActor((event.target as HTMLSelectElement).value || null),
    "Couldn't load the activity.",
  )

const more = () => run(() => store.loadMore(), "Couldn't load older activity.")
</script>

<template>
  <section class="mx-auto flex max-w-3xl flex-col gap-5" aria-labelledby="activity-title">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex flex-col gap-1">
        <RouterLink
          v-if="boardId"
          :to="{ name: 'board', params: { boardId } }"
          class="text-sm font-semibold"
          >← Back to board</RouterLink
        >
        <h1 id="activity-title" class="text-2xl font-bold tracking-tight">{{ title }}</h1>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <div role="group" aria-label="Filter activity" class="flex rounded-[12px] bg-hover p-1">
          <button
            v-for="chip in activityChips"
            :key="chip.label"
            type="button"
            :aria-pressed="store.category === chip.category"
            class="h-[34px] rounded-[10px] px-3 text-sm"
            :class="
              store.category === chip.category
                ? 'bg-panel font-semibold text-ink shadow-[0_1px_2px_rgb(17_24_39/0.08)]'
                : 'font-medium text-muted'
            "
            @click="pick(chip.category)"
          >
            {{ chip.label }}
          </button>
        </div>
        <select
          v-if="boardId && members.length"
          aria-label="Filter by member"
          class="h-[42px] rounded-[var(--radius-control)] border border-line bg-panel px-3 text-sm"
          :value="store.actorId ?? ''"
          @change="pickMember"
        >
          <option value="">Everyone</option>
          <option v-for="m in members" :key="m.userId" :value="m.userId">
            {{ m.displayName }}
          </option>
        </select>
      </div>
    </div>

    <div v-if="missing" class="flex flex-col items-center gap-2 py-16 text-center">
      <span class="text-4xl" aria-hidden="true">🔍</span>
      <p class="text-muted">This board doesn't exist or you're no longer a member.</p>
      <RouterLink to="/" class="font-semibold">Go home</RouterLink>
    </div>

    <div
      v-else-if="store.items === null"
      class="flex flex-col gap-4"
      aria-busy="true"
      aria-label="Loading activity"
    >
      <div v-for="i in 5" :key="i" class="flex items-center gap-3">
        <SkeletonBlock height="34px" width="34px" />
        <div class="flex flex-1 flex-col gap-1.5">
          <SkeletonBlock height="14px" width="70%" />
          <SkeletonBlock height="12px" width="30%" />
        </div>
      </div>
    </div>

    <div
      v-else-if="!store.items.length"
      class="fade-up flex flex-col items-center gap-2 py-16 text-center"
    >
      <span class="text-4xl" aria-hidden="true">🌱</span>
      <p class="text-muted">
        {{
          store.category || store.actorId
            ? 'Nothing matches this filter yet.'
            : 'No activity yet. Changes to cards, lists and comments show up here.'
        }}
      </p>
    </div>

    <template v-else>
      <section
        v-for="(group, g) in store.groups"
        :key="group.day"
        class="flex flex-col gap-3"
        :aria-label="group.day"
      >
        <h2 class="text-[13px] font-semibold text-muted">{{ group.day }}</h2>
        <ol class="flex flex-col gap-4">
          <li
            v-for="(entry, i) in group.items"
            :key="entry.id"
            class="fade-up"
            :style="{ '--i': Math.min(g * 3 + i, 12) }"
          >
            <ActivityRow :entry="entry" :show-board="!boardId" />
          </li>
        </ol>
      </section>
      <button
        v-if="store.nextCursor"
        type="button"
        class="ghost h-10 w-fit self-center rounded-full border border-line px-5 text-sm font-semibold"
        :disabled="store.loadingMore"
        @click="more"
      >
        {{ store.loadingMore ? 'Loading…' : 'Show older activity' }}
      </button>
    </template>
  </section>
</template>
