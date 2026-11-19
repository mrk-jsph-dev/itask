import { describe, expect, it } from 'vitest'

import { canDisconnect, connectionRows } from '../connections'

describe('connected accounts', () => {
  it('lists offered providers with their connection state', () => {
    const rows = connectionRows({
      hasPassword: true,
      logins: [{ provider: 'microsoft', accountName: 'me@outlook.com' }],
      providers: ['github', 'microsoft'],
    })

    expect(rows).toEqual([
      { provider: 'github', label: 'GitHub', connected: false, accountName: null },
      { provider: 'microsoft', label: 'Microsoft', connected: true, accountName: 'me@outlook.com' },
    ])
  })

  it('keeps a connected provider the server no longer offers, so it can be removed', () => {
    const rows = connectionRows({
      hasPassword: true,
      logins: [{ provider: 'github', accountName: 'octo' }],
      providers: [],
    })

    expect(rows.map((r) => r.provider)).toEqual(['github'])
  })

  it('never removes the last way to sign in', () => {
    expect(canDisconnect(true, 1)).toBe(true)
    expect(canDisconnect(false, 2)).toBe(true)
    expect(canDisconnect(false, 1)).toBe(false)
  })
})
