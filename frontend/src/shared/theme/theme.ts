/**
 * Colour theme helpers. `System` leaves `data-theme` off so tokens.css follows
 * `prefers-color-scheme`; `Light` / `Dark` force it. `public/theme-init.js` repeats `applyTheme`
 * before the app loads so the first paint already has the right theme.
 */
export type ThemePreference = 'System' | 'Light' | 'Dark'

export const themePreferences: readonly ThemePreference[] = ['System', 'Light', 'Dark']

export const THEME_KEY = 'itask.theme'

const isPreference = (value: unknown): value is ThemePreference =>
  themePreferences.includes(value as ThemePreference)

/** The saved choice; `System` when none is saved or storage is unavailable. */
export function readStoredTheme(storage: () => Storage = () => localStorage): ThemePreference {
  try {
    const value = storage().getItem(THEME_KEY)
    return isPreference(value) ? value : 'System'
  } catch {
    return 'System'
  }
}

/** Saves the choice for the next first paint; storage failures are ignored. */
export function storeTheme(pref: ThemePreference, storage: () => Storage = () => localStorage) {
  try {
    if (pref === 'System') storage().removeItem(THEME_KEY)
    else storage().setItem(THEME_KEY, pref)
  } catch {
    // Private mode or blocked storage: the theme still applies for this page.
  }
}

/** The theme actually shown for a preference. */
export function resolveTheme(pref: ThemePreference, systemDark: boolean): 'light' | 'dark' {
  if (pref === 'Light') return 'light'
  if (pref === 'Dark') return 'dark'
  return systemDark ? 'dark' : 'light'
}

/**
 * Sets `data-theme` on <html> and points both theme-color metas at the forced theme (each meta
 * keeps its own media query value under System).
 */
export function applyTheme(pref: ThemePreference, doc: Document = document) {
  const root = doc.documentElement
  if (pref === 'System') delete root.dataset.theme
  else root.dataset.theme = pref === 'Dark' ? 'dark' : 'light'

  doc.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]').forEach((meta) => {
    const own = meta.dataset.own ?? meta.content
    meta.dataset.own = own
    meta.content =
      pref === 'System' ? own : ((pref === 'Dark' ? meta.dataset.dark : meta.dataset.light) ?? own)
  })
}
