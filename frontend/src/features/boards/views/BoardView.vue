<script setup lang="ts">
import { ref, watch } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import BoardSettingsSheet from '../components/BoardSettingsSheet.vue'
import { errorText, useBoardsStore } from '../store'

const props = defineProps<{ boardId: string }>()
const store = useBoardsStore()

const notFound = ref(false)
const settingsOpen = ref(false)

watch(
  () => props.boardId,
  async (id) => {
    notFound.value = false
    settingsOpen.value = false
    try {
      await store.open(id)
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) notFound.value = true
      else toast(errorText(e, "Couldn't load the board."), 'error')
    }
  },
  { immediate: true },
)
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
    <p class="text-sm text-muted">Lists and cards arrive in the next modules.</p>
    <BoardSettingsSheet v-if="settingsOpen" @close="settingsOpen = false" />
  </section>

  <section v-else class="flex flex-col gap-3" aria-busy="true">
    <SkeletonBlock height="32px" width="40%" />
    <SkeletonBlock height="16px" width="25%" />
  </section>
</template>
