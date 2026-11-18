<script setup lang="ts">
import { onMounted, ref } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import { useAuthStore } from '@/features/auth/store'
import { adminApi, type AdminUser } from '../api'

const auth = useAuthStore()

const users = ref<AdminUser[] | null>(null)
const nextCursor = ref<string | null>(null)
const loadingMore = ref(false)

function failure(e: unknown, fallback: string) {
  toast(e instanceof ApiError && e.status < 500 ? e.message : fallback, 'error')
}

async function loadMore() {
  loadingMore.value = true
  try {
    const page = await adminApi.users(nextCursor.value)
    users.value = [...(users.value ?? []), ...page.items]
    nextCursor.value = page.nextCursor
  } catch (e) {
    users.value ??= []
    failure(e, "Couldn't load users.")
  } finally {
    loadingMore.value = false
  }
}

async function toggleLock(user: AdminUser) {
  try {
    await adminApi.setLocked(user.id, !user.isLocked)
    users.value = users.value!.map((u) => (u.id === user.id ? { ...u, isLocked: !u.isLocked } : u))
  } catch (e) {
    failure(e, "Couldn't change the lock.")
  }
}

const joined = (iso: string) =>
  new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })

onMounted(loadMore)
</script>

<template>
  <section class="flex flex-col gap-6">
    <div class="flex flex-col gap-1">
      <h1 class="text-2xl font-bold tracking-tight">Users</h1>
      <p class="text-sm text-muted">
        Accounts only. Admins can't open boards they aren't members of.
      </p>
    </div>

    <div v-if="users === null" class="flex flex-col gap-3" aria-busy="true">
      <SkeletonBlock v-for="n in 4" :key="n" height="48px" />
    </div>
    <ul
      v-else
      class="flex flex-col divide-y divide-line-soft rounded-[20px] border border-line-soft"
    >
      <li
        v-for="(user, i) in users"
        :key="user.id"
        class="fade-up flex flex-wrap items-center gap-3 px-4 py-3"
        :style="{ '--i': i }"
      >
        <UserAvatar :name="user.displayName" :email="user.email" :size="32" />
        <div class="flex min-w-0 flex-1 flex-col">
          <span class="truncate text-sm font-semibold">{{ user.displayName || user.email }}</span>
          <span class="truncate text-xs text-muted"
            >{{ user.email }} · joined {{ joined(user.createdAt) }}</span
          >
        </div>
        <span
          v-if="user.isPlatformAdmin"
          class="rounded-full bg-hover px-2.5 py-0.5 text-xs font-semibold"
          >Admin</span
        >
        <span
          v-if="user.isDemo"
          class="rounded-full bg-hover px-2.5 py-0.5 text-xs font-semibold text-muted"
          >Demo</span
        >
        <span
          v-if="user.isLocked"
          class="rounded-full bg-red-50 px-2.5 py-0.5 text-xs font-semibold text-red-700"
          >Locked</span
        >
        <button
          v-if="user.id !== auth.user?.id"
          type="button"
          class="ghost h-9 rounded-full border border-line px-4 text-xs font-semibold"
          :aria-label="`${user.isLocked ? 'Unlock' : 'Lock'} ${user.email}`"
          @click="toggleLock(user)"
        >
          {{ user.isLocked ? 'Unlock' : 'Lock' }}
        </button>
      </li>
    </ul>

    <button
      v-if="nextCursor"
      type="button"
      :disabled="loadingMore"
      class="ghost h-10 w-fit rounded-full border border-line px-5 text-sm font-semibold disabled:opacity-60"
      @click="loadMore"
    >
      {{ loadingMore ? 'Loading…' : 'Load more' }}
    </button>
  </section>
</template>
