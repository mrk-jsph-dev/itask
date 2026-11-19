<script setup lang="ts">
import { onMounted, ref } from 'vue'

import { ApiError } from '@/shared/api/http'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { notificationsApi, type DigestFrequency, type NotificationPreferences } from '../api'
import { digestTypeOptions } from '../format'

const prefs = ref<NotificationPreferences | null>(null)
const saving = ref(false)
const message = ref<{ ok: boolean; text: string } | null>(null)

const frequencies: { value: DigestFrequency; label: string }[] = [
  { value: 'Off', label: 'Off' },
  { value: 'Daily', label: 'Daily' },
  { value: 'Weekly', label: 'Weekly' },
]

onMounted(async () => {
  try {
    prefs.value = await notificationsApi.preferences()
  } catch {
    message.value = { ok: false, text: "Couldn't load your notification settings." }
  }
})

async function save() {
  if (!prefs.value) return
  saving.value = true
  message.value = null
  try {
    prefs.value = await notificationsApi.savePreferences({
      digest: prefs.value.digest,
      types: prefs.value.types,
    })
    message.value = { ok: true, text: 'Notification settings saved.' }
  } catch (e) {
    message.value = {
      ok: false,
      text: e instanceof ApiError && e.status < 500 ? e.message : 'Something went wrong.',
    }
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <form
    class="flex flex-col gap-4 rounded-[20px] border border-line-soft p-5"
    aria-labelledby="profile-notifications"
    @submit.prevent="save"
  >
    <div class="flex flex-col gap-1">
      <h2 id="profile-notifications" class="text-base font-semibold">🔔 Notifications</h2>
      <p class="text-sm text-muted">
        In-app notifications are always on. Choose whether you also get an email digest at 8:00 in
        your time zone.
      </p>
    </div>

    <div v-if="!prefs && !message" class="flex flex-col gap-2" aria-busy="true">
      <SkeletonBlock height="40px" width="240px" />
      <SkeletonBlock height="20px" width="60%" />
    </div>

    <template v-if="prefs">
      <fieldset class="flex flex-col gap-2">
        <legend class="mb-1.5 text-[13px] font-semibold">Email digest</legend>
        <div class="flex w-fit gap-1 rounded-[14px] bg-hover p-1">
          <label
            v-for="f in frequencies"
            :key="f.value"
            class="flex h-[34px] cursor-pointer items-center rounded-[10px] px-4 text-sm has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-accent"
            :class="
              prefs.digest === f.value
                ? 'bg-panel font-semibold text-ink shadow-[0_1px_2px_rgb(17_24_39/0.08)]'
                : 'font-medium text-[#374151]'
            "
          >
            <input
              v-model="prefs.digest"
              type="radio"
              name="digest"
              :value="f.value"
              class="sr-only"
            />
            {{ f.label }}
          </label>
        </div>
        <p v-if="prefs.digest !== 'Off' && !prefs.emailConfirmed" class="text-xs text-amber-700">
          Confirm your email address to receive digests (check your inbox for the link).
        </p>
      </fieldset>

      <fieldset class="flex flex-col gap-2" :disabled="prefs.digest === 'Off'">
        <legend class="mb-1.5 text-[13px] font-semibold">Include in the digest</legend>
        <div class="grid grid-cols-[repeat(auto-fill,minmax(200px,1fr))] gap-2">
          <label
            v-for="option in digestTypeOptions"
            :key="option.key"
            class="flex items-center gap-2.5 text-sm"
            :class="{ 'opacity-50': prefs.digest === 'Off' }"
          >
            <input
              v-model="prefs.types[option.key]"
              type="checkbox"
              class="h-4 w-4 accent-[var(--color-accent)]"
            />
            {{ option.label }}
          </label>
        </div>
      </fieldset>
    </template>

    <p
      v-if="message"
      role="status"
      class="text-sm"
      :class="message.ok ? 'text-emerald-700' : 'text-red-600'"
    >
      {{ message.text }}
    </p>
    <button
      v-if="prefs"
      type="submit"
      :disabled="saving"
      class="press h-11 w-fit rounded-full bg-accent px-6 text-sm font-semibold text-white disabled:opacity-60"
    >
      {{ saving ? 'Saving…' : 'Save notification settings' }}
    </button>
  </form>
</template>
