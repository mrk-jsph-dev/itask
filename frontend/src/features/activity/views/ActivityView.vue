<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'

import { ApiError } from '@/shared/api/http'
import PanelHeader from '@/shared/ui/PanelHeader.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { toast } from '@/shared/ui/toast'
import { useAuthStore } from '@/features/auth/store'
import ProfileCard from '@/features/profile/components/ProfileCard.vue'
import { activityApi, type BoardMemberOption } from '../api'
import ActivityRow from '../components/ActivityRow.vue'
import { activityChips } from '../format'
import { useActivityStore } from '../store'

/** Without `boardId` this is the caller's own feed across boards. */
const props = defineProps<{ boardId?: string }>()

const store = useActivityStore()
const auth = useAuthStore()
const board = ref<{ name: string; emoji: string | null } | null>(null)
const members = ref<BoardMemberOption[]>([])
const missing = ref(false)

const title = computed(() =>
  props.boardId
    ? `${board.value?.emoji ?? '⚡'} ${board.value?.name ?? 'Board'} activity`
    : 'Activity and profile',
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
  <PanelHeader :user="auth.user">
    <template #title>
      <div class="flex min-w-0 flex-col gap-1">
        <RouterLink
          v-if="boardId"
          :to="{ name: 'board', params: { boardId } }"
          class="text-sm font-semibold"
          >← Back to board</RouterLink
        >
        <h1 class="text-[22px] font-semibold">{{ title }}</h1>
      </div>
    </template>
  </PanelHeader>

  <div class="flex flex-wrap items-start gap-6 px-5 py-6 md:px-7">
    <ProfileCard v-if="!boardId && auth.user" />

    <section
      class="flex min-w-0 flex-[999_1_460px] flex-col gap-3.5"
      aria-labelledby="activity-title"
    >
      <div class="flex flex-wrap items-center gap-2.5">
        <h2 id="activity-title" class="text-lg font-semibold">⚡ Recent activity</h2>
        <div class="ml-auto flex flex-wrap items-center gap-2">
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
          <div
            role="group"
            aria-label="Filter activity"
            class="flex gap-1 rounded-[14px] bg-hover p-1"
          >
            <button
              v-for="chip in activityChips"
              :key="chip.label"
              type="button"
              :aria-pressed="store.category === chip.category"
              class="h-[34px] rounded-[10px] px-3 text-sm"
              :class="
                store.category === chip.category
                  ? 'bg-panel font-semibold text-ink shadow-card'
                  : 'font-medium text-ink-soft'
              "
              @click="pick(chip.category)"
            >
              {{ chip.label }}
            </button>
          </div>
        </div>
      </div>

      <div v-if="missing" class="flex flex-col items-center gap-2 py-16 text-center">
        <span class="text-4xl" aria-hidden="true">🔍</span>
        <p class="text-muted">This board doesn't exist or you're no longer a member.</p>
        <RouterLink to="/" class="font-semibold">Go home</RouterLink>
      </div>

      <div
        v-else-if="store.items === null"
        class="flex flex-col gap-4 rounded-[18px] border-[1.5px] border-dashed border-line px-3.5 py-4"
        aria-busy="true"
        aria-label="Loading activity"
      >
        <div v-for="i in 5" :key="i" class="flex items-center gap-3">
          <SkeletonBlock height="32px" width="32px" />
          <div class="flex flex-1 flex-col gap-1.5">
            <SkeletonBlock height="14px" width="70%" />
            <SkeletonBlock height="12px" width="30%" />
          </div>
        </div>
      </div>

      <div
        v-else-if="!store.items.length"
        class="fade-up flex flex-col items-center gap-2 rounded-[18px] border-[1.5px] border-dashed border-line py-16 text-center"
      >
        <span class="text-4xl" aria-hidden="true">🌱</span>
        <p class="px-4 text-muted">
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
          class="fade-up flex flex-col rounded-[18px] border-[1.5px] border-dashed border-line px-3.5 py-2"
          :style="{ '--i': Math.min(g, 8) }"
          :aria-label="group.day"
        >
          <h3 class="pt-2 pb-1 text-[13px] font-semibold text-muted">{{ group.day }}</h3>
          <ol class="flex flex-col">
            <li
              v-for="entry in group.items"
              :key="entry.id"
              class="border-t border-line-faint py-2.5"
            >
              <ActivityRow :entry="entry" :show-board="!boardId" />
            </li>
          </ol>
        </section>
        <button
          v-if="store.nextCursor"
          type="button"
          class="ghost flex h-10 w-fit items-center gap-2 self-center rounded-full border border-line px-5 text-sm font-semibold"
          :disabled="store.loadingMore"
          @click="more"
        >
          <span v-if="store.loadingMore" class="spinner" aria-hidden="true" />
          {{ store.loadingMore ? 'Loading…' : 'Show older activity' }}
        </button>
      </template>
    </section>
  </div>
</template>
