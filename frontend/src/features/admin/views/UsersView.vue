<script setup lang="ts">
import { onMounted, ref } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import { useAuthStore } from '@/features/auth/store'
import { adminApi, userTypes, type AdminUser, type UserType } from '../api'

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

async function changeType(user: AdminUser, event: Event) {
  const select = event.target as HTMLSelectElement
  const type = select.value as UserType
  try {
    await adminApi.setType(user.id, type)
    users.value = users.value!.map((u) =>
      u.id === user.id ? { ...u, isPlatformAdmin: type === 'PlatformAdmin' } : u,
    )
    toast(
      `${user.displayName || user.email} is now ${type === 'PlatformAdmin' ? 'a Platform admin' : 'a User'}.`,
    )
  } catch (e) {
    select.value = user.isPlatformAdmin ? 'PlatformAdmin' : 'User'
    failure(e, "Couldn't change the user type.")
  }
}

/** Why the type can't be changed for this row, or null when it can. */
function typeLocked(user: AdminUser): string | null {
  if (user.id === auth.user?.id) return "You can't change your own user type."
  if (user.isDemo) return 'Demo accounts keep their user type.'
  if (auth.user?.isDemo) return "Demo admins can't change user types."
  return null
}

const joined = (iso: string) =>
  new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })

onMounted(loadMore)
</script>

<template>
  <section class="flex flex-col gap-6 p-5 md:p-7">
    <div class="flex flex-col gap-1">
      <h1 class="text-2xl font-bold tracking-tight">Users</h1>
      <p class="text-sm text-muted">
        Accounts only. Admins can't open boards they aren't members of. A new user type applies
        within a minute.
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
          <span class="flex min-w-0 items-center gap-2">
            <span class="truncate text-sm font-semibold">{{ user.displayName || user.email }}</span>
            <span
              v-if="user.id === auth.user?.id"
              class="shrink-0 rounded-full bg-[#eef3ff] px-2 py-0.5 text-[11px] font-semibold text-accent-strong"
              >You</span
            >
            <span
              v-if="user.isDemo"
              class="shrink-0 rounded-full bg-hover px-2 py-0.5 text-[11px] font-semibold text-muted"
              >Demo</span
            >
            <span
              v-if="user.isLocked"
              class="shrink-0 rounded-full bg-red-50 px-2 py-0.5 text-[11px] font-semibold text-red-700"
              >Locked</span
            >
          </span>
          <span class="truncate text-xs text-muted"
            >{{ user.email }} · joined {{ joined(user.createdAt) }}</span
          >
        </div>
        <select
          class="h-9 w-[150px] rounded-full border px-3.5 text-xs font-semibold focus:border-accent focus:shadow-[0_0_0_3px_rgb(29_91_245/0.15)] focus:outline-none disabled:opacity-70"
          :class="
            user.isPlatformAdmin
              ? 'border-[#c9d7fb] bg-[#eef3ff] text-accent-strong'
              : 'border-line bg-panel text-ink'
          "
          :value="user.isPlatformAdmin ? 'PlatformAdmin' : 'User'"
          :disabled="!!typeLocked(user)"
          :title="typeLocked(user) ?? undefined"
          :aria-label="`User type for ${user.email}`"
          @change="changeType(user, $event)"
        >
          <option v-for="option in userTypes" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>
        <button
          v-if="user.id !== auth.user?.id"
          type="button"
          class="lock-button flex h-9 w-[104px] items-center justify-center gap-1.5 rounded-full border text-xs font-semibold"
          :class="
            user.isLocked
              ? 'border-transparent bg-accent text-white hover:bg-accent-strong'
              : 'border-line bg-panel text-ink hover:border-red-200 hover:bg-red-50 hover:text-red-700'
          "
          :aria-label="`${user.isLocked ? 'Unlock' : 'Lock'} ${user.email}`"
          @click="toggleLock(user)"
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2.2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <rect x="5" y="11" width="14" height="10" rx="2.5" />
            <path :d="user.isLocked ? 'M8 11V8a4 4 0 017.6-1.7' : 'M8 11V8a4 4 0 018 0v3'" />
          </svg>
          {{ user.isLocked ? 'Unlock' : 'Lock' }}
        </button>
        <span v-else class="hidden w-[104px] sm:block" aria-hidden="true" />
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

<style scoped>
.lock-button {
  transition:
    background-color var(--d-fast) var(--ease-out),
    border-color var(--d-fast) var(--ease-out),
    color var(--d-fast) var(--ease-out),
    transform var(--d-fast) var(--ease-out);
}
.lock-button:active {
  transform: scale(0.96);
}
</style>
