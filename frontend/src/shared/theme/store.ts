import { defineStore } from 'pinia'
import { ref } from 'vue'

import { api } from '@/shared/api/http'
import { applyTheme, readStoredTheme, storeTheme, type ThemePreference } from './theme'

/**
 * The theme preference. localStorage gives the first paint; for a signed-in (non-demo) user the
 * profile value wins once `/me` loads, and changes are saved back so they follow the user.
 */
export const useThemeStore = defineStore('theme', () => {
  const preference = ref<ThemePreference>(readStoredTheme())
  /** Whether choices are saved on the profile (signed in, not a shared demo account). */
  const synced = ref(false)

  function show(pref: ThemePreference) {
    preference.value = pref
    storeTheme(pref)
    applyTheme(pref)
  }

  /** Applies a choice at once; resolves when the profile has it (rejects if that save fails). */
  async function choose(pref: ThemePreference) {
    show(pref)
    if (synced.value) await api.patch('/me', { themePreference: pref })
  }

  /** Called after `/me` loads: the server value wins unless the account can't save one. */
  function adopt(server: ThemePreference | undefined, canSync: boolean) {
    synced.value = canSync
    if (canSync && server && server !== preference.value) show(server)
  }

  /** Signed out: keep the theme on this device, stop saving it. */
  function detach() {
    synced.value = false
  }

  return { preference, synced, choose, adopt, detach }
})
