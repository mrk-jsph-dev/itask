<script setup lang="ts">
import { computed } from 'vue'

import type { Card } from '../api'

const props = defineProps<{
  card: Pick<Card, 'blocked' | 'blockerCount' | 'blockingCount'>
  /** Also show the muted "blocks N" text for cards others wait on. */
  showBlocking?: boolean
}>()

const plural = (n: number) => `${n} ${n === 1 ? 'card' : 'cards'}`
const blockedLabel = computed(() => `Blocked by ${plural(props.card.blockerCount)}`)
const blockingLabel = computed(() => `Blocks ${plural(props.card.blockingCount)}`)
</script>

<template>
  <span
    v-if="card.blocked"
    class="inline-flex items-center gap-1 rounded-lg bg-[#FEF2F2] px-2 py-[3px] text-[12px] font-semibold text-[#B91C1C]"
    role="img"
    :aria-label="blockedLabel"
    :title="blockedLabel"
    data-testid="blocked-chip"
  >
    <span aria-hidden="true">🔒</span>{{ card.blockerCount }}
  </span>
  <span
    v-if="showBlocking && card.blockingCount"
    class="text-[12px] text-muted"
    :aria-label="blockingLabel"
    :title="blockingLabel"
  >
    blocks {{ card.blockingCount }}
  </span>
</template>
