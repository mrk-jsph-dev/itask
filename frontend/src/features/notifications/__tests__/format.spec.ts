import { describe, expect, it } from 'vitest'

import { badgeText, describeNotification, inviteIdOf } from '../format'
import { notification } from './fixtures'

describe('describeNotification', () => {
  it('links card notifications to the card sheet', () => {
    const line = describeNotification(notification())

    expect(line).toMatchObject({ actor: 'Ada', what: 'mentioned you on', target: 'Fix login' })
    expect(line.to).toEqual({ name: 'card', params: { boardId: 'b1', cardId: 'card1' } })
  })

  it.each([
    ['assigned', 'assigned you to'],
    ['comment_on_my_card', 'commented on'],
  ] as const)('%s reads "%s"', (type, what) => {
    expect(describeNotification(notification({ type })).what).toBe(what)
  })

  it('starts reminders with the card and has no actor', () => {
    const due = describeNotification(
      notification({
        type: 'due_soon',
        actor: null,
        data: { card: { key: 'ITK-1', title: 'Fix login' }, dueAt: '2026-11-20T15:00:00Z' },
      }),
    )
    const overdue = describeNotification(notification({ type: 'overdue', actor: null }))

    expect(due).toMatchObject({ actor: null, what: 'Due soon:', target: 'Fix login' })
    expect(due.suffix).toMatch(/^ · \w{3} /)
    expect(overdue).toMatchObject({ emoji: '🔥', what: 'Overdue:', suffix: null })
  })

  it('names the board for invites and leaves the link to the Accept button', () => {
    const n = notification({
      type: 'invited',
      cardId: null,
      data: { inviteId: 'i1', role: 'Editor' },
    })

    expect(describeNotification(n)).toMatchObject({
      what: 'invited you to',
      target: 'Launch',
      to: null,
    })
    expect(inviteIdOf(n)).toBe('i1')
    expect(inviteIdOf(notification())).toBeNull()
  })
})

describe('badgeText', () => {
  it('hides at zero and caps at 9+', () => {
    expect([0, 1, 9, 10, 250].map(badgeText)).toEqual([null, '1', '9', '9+', '9+'])
  })
})
