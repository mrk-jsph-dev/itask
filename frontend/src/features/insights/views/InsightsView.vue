<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import PanelHeader from '@/shared/ui/PanelHeader.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { useAuthStore } from '@/features/auth/store'

import { insightsApi, ranges, type InsightRange } from '../api'
import { dayBars, groupBars, groupSize, isEmpty, listBars, parseRange, tiles } from '../chart'
import AgingList from '../components/AgingList.vue'
import ThroughputChart from '../components/ThroughputChart.vue'
import TimeInListChart from '../components/TimeInListChart.vue'
import { useInsightsStore } from '../store'

const props = defineProps<{ boardId: string }>()

const auth = useAuthStore()
const store = useInsightsStore()
const route = useRoute()
const router = useRouter()
const board = ref<{ name: string; emoji: string | null } | null>(null)

const days = computed(() => parseRange(route.query.days))

watch(
  () => props.boardId,
  (id) => {
    board.value = null
    void insightsApi
      .boardName(id)
      .then((b) => (board.value = b))
      .catch(() => {})
  },
  { immediate: true },
)
watch([() => props.boardId, days], ([id, d]) => void store.load(id, d), { immediate: true })

function pick(range: InsightRange) {
  void router.replace({ query: { ...route.query, days: range === 30 ? undefined : String(range) } })
}

const data = computed(() => store.data)
const statTiles = computed(() => (data.value ? tiles(data.value) : []))
const unit = computed(() => (groupSize(days.value) === 1 ? 'day' : 'week'))
const groups = computed(() =>
  data.value ? groupBars(dayBars(data.value), groupSize(data.value.days)) : [],
)
const lists = computed(() => (data.value ? listBars(data.value) : []))
const empty = computed(() => (data.value ? isEmpty(data.value) : false))
</script>

<template>
  <PanelHeader :user="auth.user">
    <template #title>
      <div class="flex min-w-0 flex-col gap-1">
        <RouterLink :to="{ name: 'board', params: { boardId } }" class="text-sm font-semibold"
          >← Back to board</RouterLink
        >
        <h1 class="truncate text-[22px] font-semibold">
          {{ board?.emoji ?? '📈' }} {{ board?.name ?? 'Board' }} insights
        </h1>
      </div>
    </template>
  </PanelHeader>

  <div class="flex flex-col gap-5 px-5 py-6 md:px-7">
    <div v-if="store.missing" class="flex flex-col items-center gap-2 py-16 text-center">
      <span class="text-4xl" aria-hidden="true">🔍</span>
      <p class="text-muted">This board doesn't exist or you're no longer a member.</p>
      <RouterLink to="/" class="font-semibold">Go home</RouterLink>
    </div>

    <template v-else>
      <div class="flex flex-wrap items-center gap-2.5">
        <h2 class="text-lg font-semibold">📈 How work flows</h2>
        <div
          role="group"
          aria-label="Time range"
          class="ml-auto flex gap-1 rounded-[14px] bg-hover p-1"
        >
          <button
            v-for="r in ranges"
            :key="r"
            type="button"
            :aria-pressed="days === r"
            class="h-[34px] rounded-[10px] px-3 text-sm"
            :class="
              days === r
                ? 'bg-panel font-semibold text-ink shadow-[0_1px_2px_rgb(17_24_39/0.08)]'
                : 'font-medium text-[#374151]'
            "
            @click="pick(r)"
          >
            {{ r }} days
          </button>
        </div>
      </div>

      <p v-if="store.failed && !data" class="py-10 text-center text-muted" role="alert">
        Couldn't load insights. Try again in a moment.
      </p>

      <div
        v-else-if="!data"
        class="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(150px,1fr))]"
        aria-busy="true"
        aria-label="Loading insights"
      >
        <SkeletonBlock v-for="i in 4" :key="i" height="96px" />
        <div class="col-span-full"><SkeletonBlock height="240px" /></div>
      </div>

      <template v-else>
        <dl
          class="grid gap-3 [grid-template-columns:repeat(auto-fit,minmax(150px,1fr))]"
          :aria-busy="store.loading"
          data-testid="insight-tiles"
        >
          <div
            v-for="(t, i) in statTiles"
            :key="t.label"
            class="fade-up flex flex-col gap-1 rounded-[18px] border border-line bg-panel px-4 py-3.5"
            :style="{ '--i': i }"
          >
            <dt class="text-[13px] font-medium text-muted">{{ t.label }}</dt>
            <dd class="text-[26px] leading-tight font-semibold tabular-nums text-ink">
              <span v-if="t.warn" aria-hidden="true" class="text-xl">⚠️ </span>{{ t.value }}
            </dd>
            <dd class="text-xs" :class="t.warn ? 'font-semibold text-[#b45309]' : 'text-muted'">
              {{ t.hint }}
            </dd>
          </div>
        </dl>

        <div
          v-if="empty"
          class="fade-up flex flex-col items-center gap-2 rounded-[18px] border-[1.5px] border-dashed border-line py-14 text-center"
        >
          <span class="text-4xl" aria-hidden="true">🌱</span>
          <p class="px-4 text-muted">Insights appear after a few cards move.</p>
        </div>

        <div
          v-else
          class="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(min(100%,420px),1fr))]"
        >
          <section
            class="fade-up col-span-full flex min-w-0 flex-col gap-3 rounded-[18px] border border-line bg-panel px-4 py-4"
            style="--i: 4"
            aria-labelledby="throughput-title"
          >
            <h3 id="throughput-title" class="font-semibold">Done vs created</h3>
            <ThroughputChart :groups="groups" :unit="unit" />
          </section>

          <section
            class="fade-up flex min-w-0 flex-col gap-3 rounded-[18px] border border-line bg-panel px-4 py-4"
            style="--i: 5"
            aria-labelledby="lists-title"
          >
            <div>
              <h3 id="lists-title" class="font-semibold">Time in list</h3>
              <p class="text-xs text-muted">Median days a card stays, with open cards and WIP</p>
            </div>
            <TimeInListChart :bars="lists" :wip="data.wip" />
          </section>

          <section
            class="fade-up flex min-w-0 flex-col gap-2 rounded-[18px] border border-line bg-panel px-4 py-4"
            style="--i: 6"
            aria-labelledby="aging-title"
          >
            <div>
              <h3 id="aging-title" class="font-semibold">Waiting longest</h3>
              <p class="text-xs text-muted">Open cards by days in their current list</p>
            </div>
            <AgingList :board-id="boardId" :cards="data.aging" />
          </section>
        </div>
      </template>
    </template>
  </div>
</template>
