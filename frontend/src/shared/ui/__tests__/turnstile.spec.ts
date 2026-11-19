import { afterEach, describe, expect, it } from 'vitest'

import { loadTurnstile } from '../turnstile'

describe('loadTurnstile', () => {
  afterEach(() => document.head.querySelectorAll('script').forEach((s) => s.remove()))

  it('adds the Cloudflare script only once', async () => {
    const first = loadTurnstile()
    const second = loadTurnstile()
    const scripts = document.head.querySelectorAll('script[src*="challenges.cloudflare.com"]')
    expect(scripts).toHaveLength(1)
    expect(second).toBe(first)
    scripts[0]!.dispatchEvent(new Event('load'))
    await expect(first).resolves.toBeUndefined()
    await loadTurnstile()
    expect(document.head.querySelectorAll('script[src*="challenges.cloudflare.com"]')).toHaveLength(1)
  })
})
