<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

import { ApiError } from '@/shared/api/http'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { toast } from '@/shared/ui/toast'
import { useAuthStore } from '@/features/auth/store'
import { profileApi, type MyStats } from '../api'

const auth = useAuthStore()
const router = useRouter()

const user = computed(() => auth.user!)
const displayName = ref(user.value.displayName)
const saving = ref(false)
/** Null while loading; stays null when the request fails. */
const stats = ref<MyStats | null>(null)
const statsFailed = ref(false)

const memberSince = computed(() =>
  new Date(user.value.createdAt).toLocaleDateString('en', { month: 'short', year: 'numeric' }),
)
const tiles = computed(() =>
  stats.value
    ? [
        { label: 'Boards', value: stats.value.boards, accent: false },
        { label: 'Tasks', value: stats.value.tasks, accent: false },
        { label: 'Done', value: stats.value.done, accent: true },
      ]
    : [],
)

onMounted(() => {
  profileApi
    .stats()
    .then((s) => (stats.value = s))
    .catch(() => (statsFailed.value = true))
})

const inputClass =
  'h-11 rounded-[var(--radius-control)] border border-line bg-panel px-3.5 text-[15px] font-normal read-only:bg-hover read-only:text-muted'

async function save() {
  const name = displayName.value.trim()
  if (!name || name === user.value.displayName) return
  saving.value = true
  try {
    const saved = await profileApi.update({
      displayName: name,
      timeZone: user.value.timeZone,
      avatarColor: user.value.avatarColor,
    })
    auth.patchUser(saved)
    displayName.value = saved.displayName
    toast('Profile saved.')
  } catch (e) {
    toast(
      e instanceof ApiError && e.status < 500 ? e.message : "Couldn't save your profile.",
      'error',
    )
  } finally {
    saving.value = false
  }
}

async function signOut() {
  try {
    await auth.signOut()
  } catch {
    // The store has already cleared the local session.
  }
  await router.replace('/sign-in')
}
</script>

<template>
  <section
    class="fade-up flex w-full max-w-[360px] min-w-0 flex-[1_1_300px] flex-col gap-4 rounded-[20px] bg-sunken p-[22px]"
    aria-labelledby="profile-card-title"
  >
    <div class="flex items-center gap-3.5">
      <UserAvatar
        :name="user.displayName"
        :email="user.email"
        :color="user.avatarColor"
        :size="60"
      />
      <div class="flex min-w-0 flex-col gap-0.5">
        <h2 id="profile-card-title" class="truncate text-lg font-bold">
          {{ user.displayName || 'Your profile' }}
        </h2>
        <span class="text-[13px] text-muted">Member since {{ memberSince }}</span>
        <span
          v-if="user.isDemo"
          class="w-fit rounded-full bg-panel px-2.5 py-0.5 text-xs font-semibold text-muted"
          >Demo account</span
        >
      </div>
    </div>

    <dl v-if="!statsFailed" class="grid grid-cols-3 gap-2" aria-label="Your numbers">
      <template v-if="stats">
        <div
          v-for="tile in tiles"
          :key="tile.label"
          class="flex flex-col-reverse rounded-xl bg-panel p-2.5"
        >
          <dt class="text-xs text-muted">{{ tile.label }}</dt>
          <dd class="text-xl font-bold" :class="{ 'text-accent-strong': tile.accent }">
            {{ tile.value }}
          </dd>
        </div>
      </template>
      <template v-else>
        <SkeletonBlock v-for="n in 3" :key="n" height="58px" />
      </template>
    </dl>

    <form class="flex flex-col gap-4" @submit.prevent="save">
      <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
        Display name
        <input
          v-model="displayName"
          type="text"
          autocomplete="name"
          maxlength="100"
          required
          placeholder="Your name"
          :class="inputClass"
        />
      </label>
      <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
        Email
        <input :value="user.email" type="email" readonly :class="inputClass" />
      </label>
      <button
        type="submit"
        class="press flex h-11 items-center justify-center gap-2 rounded-full bg-accent font-semibold text-on-accent disabled:opacity-60"
        :disabled="saving || !displayName.trim() || displayName.trim() === user.displayName"
      >
        <span v-if="saving" class="spinner" aria-hidden="true" />
        Save changes
      </button>
    </form>
    <RouterLink to="/profile" class="text-center text-sm font-semibold no-underline">
      Password, colour and time zone
    </RouterLink>
    <button type="button" class="text-sm font-semibold text-accent-strong" @click="signOut">
      Sign out
    </button>
  </section>
</template>
