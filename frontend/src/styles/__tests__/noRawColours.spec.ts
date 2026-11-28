import { describe, expect, it } from 'vitest'

/**
 * Components take colours from the theme tokens in styles/tokens.css only, so the dark theme
 * covers everything. Raw hex, rgb()/hsl() and Tailwind palette utilities (bg-white, text-gray-500,
 * ...) fail here; use a token utility (bg-panel, text-muted, text-danger, ...) instead.
 */
const sources = import.meta.glob<string>(['/src/**/*.vue', '/src/**/*.ts', '!/src/**/__tests__/**'], {
  query: '?raw',
  import: 'default',
  eager: true,
})

/** Files whose colours are data or a third-party brand, not UI. */
const allowed = new Set([
  '/src/shared/ui/palette.ts', // colours users pick for avatars, boards and labels
  '/src/features/auth/components/ExternalButtons.vue', // Microsoft logo squares
])

const palette =
  'white|black|slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose'
const rules: { name: string; pattern: RegExp }[] = [
  { name: 'hex colour', pattern: /#[0-9a-fA-F]{3,8}\b(?![-\w])/ },
  { name: 'rgb()/hsl() colour', pattern: /\b(?:rgba?|hsla?|oklch|oklab)\(/ },
  {
    name: 'Tailwind palette colour',
    pattern: new RegExp(
      `(?<![\\w-])(?:bg|text|border|ring|fill|stroke|outline|divide|from|via|to|decoration|caret|placeholder|shadow)-(?:${palette})\\b`,
    ),
  },
]

export function findRawColours(files: Record<string, string>) {
  const hits: string[] = []
  for (const [path, text] of Object.entries(files)) {
    if (allowed.has(path)) continue
    text.split('\n').forEach((line, i) => {
      for (const rule of rules) {
        if (rule.pattern.test(line)) hits.push(`${path}:${i + 1} ${rule.name}: ${line.trim()}`)
      }
    })
  }
  return hits
}

describe('no raw colours', () => {
  it('scans the app sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('flags hex, rgb() and palette utilities', () => {
    expect(
      findRawColours({
        'a.vue': '<div class="text-[#374151]">',
        'b.vue': "shadow: '0 1px 2px rgb(0 0 0 / .1)'",
        'c.vue': '<p class="bg-white text-gray-500">',
        'd.vue': '<p class="bg-panel text-muted text-accent-strong border-line-soft">',
      }),
    ).toHaveLength(3)
  })

  it('finds none in components (use the tokens in styles/tokens.css)', () => {
    expect(findRawColours(sources)).toEqual([])
  })
})
