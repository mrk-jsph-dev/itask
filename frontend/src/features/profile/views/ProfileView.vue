<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import PanelHeader from '@/shared/ui/PanelHeader.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { ApiError } from '@/shared/api/http'
import { useAuthStore } from '@/features/auth/store'
import DigestPreferences from '@/features/notifications/components/DigestPreferences.vue'
import { passwordStrength } from '@/features/auth/passwordStrength'
import AppearanceCard from '../components/AppearanceCard.vue'
import ConnectedAccounts from '../components/ConnectedAccounts.vue'
import { profileApi } from '../api'
import { avatarColors, timeZones } from '../options'

const auth = useAuthStore()
const router = useRouter()

const user = computed(() => auth.user!)
const zones = timeZones()

const displayName = ref(user.value.displayName)
const timeZone = ref(user.value.timeZone)
const avatarColor = ref(user.value.avatarColor)
const saving = ref(false)
const profileMessage = ref<{ ok: boolean; text: string } | null>(null)

const currentPassword = ref('')
const newPassword = ref('')
const changing = ref(false)
const passwordMessage = ref<{ ok: boolean; text: string } | null>(null)
const strength = computed(() => passwordStrength(newPassword.value))

const memberSince = computed(() =>
  new Date(user.value.createdAt).toLocaleDateString(undefined, {
    month: 'short',
    year: 'numeric',
  }),
)

const inputClass =
  'h-[46px] rounded-[var(--radius-control)] border border-line bg-panel px-3.5 text-[15px] font-normal'

function messages(e: unknown): string {
  if (e instanceof ApiError && e.errors) return Object.values(e.errors).flat().join(' ')
  if (e instanceof ApiError && e.status < 500) return e.message
  return 'Something went wrong.'
}

async function saveProfile() {
  saving.value = true
  profileMessage.value = null
  try {
    const saved = await profileApi.update({
      displayName: displayName.value.trim(),
      timeZone: timeZone.value,
      avatarColor: avatarColor.value,
    })
    auth.patchUser(saved)
    displayName.value = saved.displayName
    profileMessage.value = { ok: true, text: 'Profile saved.' }
  } catch (e) {
    profileMessage.value = { ok: false, text: messages(e) }
  } finally {
    saving.value = false
  }
}

async function changePassword() {
  changing.value = true
  passwordMessage.value = null
  try {
    if (user.value.hasPassword) {
      await profileApi.changePassword(currentPassword.value, newPassword.value)
    } else {
      await profileApi.setPassword(newPassword.value)
      auth.patchUser({ hasPassword: true })
    }
    const text = currentPassword.value ? 'Password changed.' : 'Password set.'
    currentPassword.value = ''
    newPassword.value = ''
    passwordMessage.value = { ok: true, text }
  } catch (e) {
    passwordMessage.value = { ok: false, text: messages(e) }
  } finally {
    changing.value = false
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
  <PanelHeader title="Your profile" :user="auth.user" />
  <section class="flex w-full max-w-2xl flex-col gap-6 px-5 py-6 md:px-7">
    <div
      class="fade-up flex items-center gap-4 rounded-[20px] border border-line-soft p-5"
      style="--i: 0"
    >
      <UserAvatar :name="displayName" :email="user.email" :color="avatarColor" :size="64" />
      <div class="flex min-w-0 flex-col">
        <span class="truncate text-lg font-semibold">{{ displayName || user.email }}</span>
        <span class="truncate text-sm text-muted">{{ user.email }}</span>
        <span class="text-xs text-muted">Member since {{ memberSince }}</span>
        <span
          v-if="user.isDemo"
          class="mt-1 w-fit rounded-full bg-hover px-2.5 py-0.5 text-xs font-semibold text-muted"
          >Demo account</span
        >
      </div>
    </div>

    <form
      class="fade-up flex flex-col gap-4 rounded-[20px] border border-line-soft p-5"
      style="--i: 1"
      aria-labelledby="profile-details"
      @submit.prevent="saveProfile"
    >
      <h2 id="profile-details" class="text-base font-semibold">Details</h2>
      <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
        Display name
        <input
          v-model="displayName"
          type="text"
          autocomplete="name"
          maxlength="100"
          required
          :class="inputClass"
        />
      </label>
      <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
        Time zone
        <select v-model="timeZone" :class="inputClass">
          <option v-for="zone in zones" :key="zone" :value="zone">{{ zone }}</option>
        </select>
      </label>
      <fieldset class="flex flex-col gap-2">
        <legend class="mb-1.5 text-[13px] font-semibold">Avatar colour</legend>
        <div class="flex flex-wrap gap-2">
          <label
            v-for="color in avatarColors"
            :key="color"
            class="press relative flex h-9 w-9 cursor-pointer items-center justify-center rounded-full"
            :style="{ backgroundColor: color }"
          >
            <input
              v-model="avatarColor"
              type="radio"
              name="avatar-color"
              :value="color"
              class="peer sr-only"
              :aria-label="`Colour ${color}`"
            />
            <span class="hidden text-sm font-bold text-on-accent peer-checked:block" aria-hidden="true"
              >✓</span
            >
            <span
              class="pointer-events-none absolute -inset-1 rounded-full peer-focus-visible:outline-2 peer-focus-visible:outline-accent"
            />
          </label>
        </div>
      </fieldset>
      <p
        v-if="profileMessage"
        role="status"
        class="text-sm"
        :class="profileMessage.ok ? 'text-success' : 'text-danger'"
      >
        {{ profileMessage.text }}
      </p>
      <button
        type="submit"
        :disabled="saving"
        class="press h-11 w-fit rounded-full bg-accent px-6 text-sm font-semibold text-on-accent disabled:opacity-60"
      >
        {{ saving ? 'Saving…' : 'Save changes' }}
      </button>
    </form>

    <AppearanceCard class="fade-up" style="--i: 2" />

    <DigestPreferences class="fade-up" style="--i: 2" />

    <ConnectedAccounts
      v-if="!user.isDemo"
      class="fade-up"
      style="--i: 3"
      :has-password="user.hasPassword"
    />

    <form
      class="fade-up flex flex-col gap-4 rounded-[20px] border border-line-soft p-5"
      style="--i: 4"
      aria-labelledby="profile-password"
      @submit.prevent="changePassword"
    >
      <h2 id="profile-password" class="text-base font-semibold">Password</h2>
      <p v-if="user.isDemo" class="text-sm text-muted">
        Demo accounts can't change their password.
      </p>
      <template v-else>
        <p v-if="!user.hasPassword" class="text-sm text-muted">
          You sign in with a connected account. Set a password to sign in with your email too.
        </p>
        <label v-if="user.hasPassword" class="flex flex-col gap-1.5 text-[13px] font-semibold">
          Current password
          <input
            v-model="currentPassword"
            type="password"
            autocomplete="current-password"
            required
            :class="inputClass"
          />
        </label>
        <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
          New password
          <input
            v-model="newPassword"
            type="password"
            autocomplete="new-password"
            aria-describedby="new-password-hint"
            required
            :class="inputClass"
          />
        </label>
        <span id="new-password-hint" class="-mt-2 text-xs text-muted">{{ strength.hint }}</span>
        <p
          v-if="passwordMessage"
          role="status"
          class="text-sm"
          :class="passwordMessage.ok ? 'text-success' : 'text-danger'"
        >
          {{ passwordMessage.text }}
        </p>
        <button
          type="submit"
          :disabled="changing || strength.score < 4"
          class="press h-11 w-fit rounded-full bg-ink px-6 text-sm font-semibold text-panel disabled:opacity-60"
        >
          <template v-if="user.hasPassword">{{
            changing ? 'Changing…' : 'Change password'
          }}</template>
          <template v-else>{{ changing ? 'Saving…' : 'Set password' }}</template>
        </button>
      </template>
    </form>

    <button
      type="button"
      class="ghost h-11 w-fit rounded-full border border-line px-6 text-sm font-semibold"
      @click="signOut"
    >
      Sign out
    </button>
  </section>
</template>
