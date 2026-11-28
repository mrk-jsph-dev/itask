<script setup lang="ts">
import { computed } from 'vue'

import { toast } from '@/shared/ui/toast'
import { useThemeStore } from '@/shared/theme/store'
import { themePreferences, type ThemePreference } from '@/shared/theme/theme'

const theme = useThemeStore()

const labels: Record<ThemePreference, string> = { System: 'System', Light: 'Light', Dark: 'Dark' }

const selected = computed({
  get: () => theme.preference,
  set: (pref: ThemePreference) => {
    theme.choose(pref).catch(() => toast('Couldn’t save the theme to your profile.', 'error'))
  },
})
</script>

<template>
  <section
    class="flex flex-col gap-4 rounded-[20px] border border-line-soft p-5"
    aria-labelledby="profile-appearance"
  >
    <h2 id="profile-appearance" class="text-base font-semibold">Appearance</h2>
    <fieldset class="flex flex-col gap-2">
      <legend class="mb-1.5 text-[13px] font-semibold">Theme</legend>
      <div class="flex w-fit gap-1 rounded-[14px] bg-hover p-1">
        <label
          v-for="pref in themePreferences"
          :key="pref"
          class="flex h-[34px] cursor-pointer items-center rounded-[10px] px-4 text-sm has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-accent"
          :class="
            selected === pref
              ? 'bg-panel font-semibold text-ink shadow-card'
              : 'font-medium text-ink-soft'
          "
        >
          <input v-model="selected" type="radio" name="theme" :value="pref" class="sr-only" />
          {{ labels[pref] }}
        </label>
      </div>
      <p class="text-xs text-muted">
        <template v-if="theme.synced">System follows your device. Saved to your profile.</template>
        <template v-else>System follows your device. Saved on this device only.</template>
      </p>
    </fieldset>
  </section>
</template>
