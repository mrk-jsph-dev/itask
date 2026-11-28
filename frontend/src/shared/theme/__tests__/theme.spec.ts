import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { api } from '@/shared/api/http'
import { useThemeStore } from '../store'
import { THEME_KEY, applyTheme, readStoredTheme, resolveTheme, storeTheme } from '../theme'

const broken = () =>
  ({
    getItem: () => {
      throw new Error('blocked')
    },
    setItem: () => {
      throw new Error('blocked')
    },
    removeItem: () => {
      throw new Error('blocked')
    },
  }) as unknown as Storage

function metas() {
  document.head.innerHTML = `
    <meta name="theme-color" media="(prefers-color-scheme: light)" content="#light" data-light="#light" data-dark="#dark">
    <meta name="theme-color" media="(prefers-color-scheme: dark)" content="#dark" data-light="#light" data-dark="#dark">`
}

describe('theme helpers', () => {
  beforeEach(() => {
    localStorage.clear()
    delete document.documentElement.dataset.theme
  })

  it('reads System when nothing or junk is stored', () => {
    expect(readStoredTheme()).toBe('System')
    localStorage.setItem(THEME_KEY, 'Sepia')
    expect(readStoredTheme()).toBe('System')
  })

  it('stores Light and Dark, and clears the key for System', () => {
    storeTheme('Dark')
    expect(readStoredTheme()).toBe('Dark')
    storeTheme('System')
    expect(localStorage.getItem(THEME_KEY)).toBeNull()
  })

  it('ignores storage failures', () => {
    expect(readStoredTheme(broken)).toBe('System')
    expect(() => storeTheme('Dark', broken)).not.toThrow()
  })

  it('resolves System from the device and forces Light/Dark', () => {
    expect(resolveTheme('System', true)).toBe('dark')
    expect(resolveTheme('System', false)).toBe('light')
    expect(resolveTheme('Light', true)).toBe('light')
    expect(resolveTheme('Dark', false)).toBe('dark')
  })

  it('sets data-theme and points both theme-color metas at the forced theme', () => {
    metas()
    applyTheme('Dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    const contents = () =>
      [...document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')].map(
        (m) => m.content,
      )
    expect(contents()).toEqual(['#dark', '#dark'])

    applyTheme('Light')
    expect(document.documentElement.dataset.theme).toBe('light')
    expect(contents()).toEqual(['#light', '#light'])

    applyTheme('System')
    expect(document.documentElement.dataset.theme).toBeUndefined()
    expect(contents()).toEqual(['#light', '#dark'])
  })
})

describe('theme store', () => {
  beforeEach(() => {
    localStorage.clear()
    delete document.documentElement.dataset.theme
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('starts from the stored choice', () => {
    localStorage.setItem(THEME_KEY, 'Dark')
    expect(useThemeStore().preference).toBe('Dark')
  })

  it('applies a choice at once and only saves it to the profile once synced', async () => {
    const patch = vi.spyOn(api, 'patch').mockResolvedValue(undefined)
    const theme = useThemeStore()

    await theme.choose('Dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem(THEME_KEY)).toBe('Dark')
    expect(patch).not.toHaveBeenCalled()

    theme.adopt('Dark', true)
    await theme.choose('Light')
    expect(patch).toHaveBeenCalledWith('/me', { themePreference: 'Light' })
  })

  it('lets the server value win after /me loads', () => {
    localStorage.setItem(THEME_KEY, 'Light')
    const theme = useThemeStore()

    theme.adopt('Dark', true)

    expect(theme.preference).toBe('Dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem(THEME_KEY)).toBe('Dark')
  })

  it('keeps the local choice for demo accounts and never saves it', async () => {
    const patch = vi.spyOn(api, 'patch').mockResolvedValue(undefined)
    localStorage.setItem(THEME_KEY, 'Dark')
    const theme = useThemeStore()

    theme.adopt('System', false)
    await theme.choose('Light')

    expect(theme.preference).toBe('Light')
    expect(patch).not.toHaveBeenCalled()
  })

  it('stops saving after sign-out', async () => {
    const patch = vi.spyOn(api, 'patch').mockResolvedValue(undefined)
    const theme = useThemeStore()
    theme.adopt('System', true)
    theme.detach()

    await theme.choose('Dark')

    expect(patch).not.toHaveBeenCalled()
  })

  it('rejects when the profile save fails but keeps the theme', async () => {
    vi.spyOn(api, 'patch').mockRejectedValue(new Error('offline'))
    const theme = useThemeStore()
    theme.adopt('System', true)

    await expect(theme.choose('Dark')).rejects.toThrow('offline')
    expect(document.documentElement.dataset.theme).toBe('dark')
  })
})
