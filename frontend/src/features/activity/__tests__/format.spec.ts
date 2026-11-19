import { describe, expect, it } from 'vitest'

import type { ActivityEntry } from '../api'
import { categoryOf, dayLabel, describeActivity, groupByDay, joinNames } from '../format'

const card = { key: 'ITK-7', title: 'Fix login' }

const line = (type: string, data: Record<string, unknown> = {}) => {
  const { emoji, what, target } = describeActivity({ type, data })
  return { emoji, text: [what, target].filter(Boolean).join(' ') }
}

describe('describeActivity', () => {
  it.each([
    ['board.created', {}, 'created the board'],
    ['board.created', { template: 'Sprint' }, 'created the board from the Sprint template'],
    [
      'board.updated',
      { name: 'New', from: 'Old', fields: ['name'] },
      'renamed the board from “Old” to New',
    ],
    ['board.updated', { fields: ['color', 'emoji'] }, 'changed the board colour and emoji'],
    ['board.archived', {}, 'archived the board'],
    ['board.restored', {}, 'restored the board'],
    ['member.joined', { member: { name: 'Ada' }, role: 'Editor' }, 'joined as Editor'],
    ['member.left', { member: { name: 'Ada' } }, 'left the board'],
    ['member.removed', { member: { name: 'Ada' } }, 'removed Ada'],
    [
      'member.role_changed',
      { member: { name: 'Ada' }, from: 'Editor', to: 'Viewer' },
      'made Ada Viewer',
    ],
    ['list.created', { list: 'QA' }, 'added the list QA'],
    [
      'list.renamed',
      { list: 'Doing', from: 'In progress' },
      'renamed the list “In progress” to Doing',
    ],
    ['list.updated', { list: 'QA' }, 'updated the list QA'],
    ['list.moved', { list: 'QA' }, 'reordered the list QA'],
    ['list.archived', { list: 'QA' }, 'archived the list QA'],
    ['list.restored', { list: 'QA' }, 'restored the list QA'],
    ['card.created', { card, list: 'To do' }, 'created in To do Fix login'],
    [
      'card.created',
      { card, list: 'To do', recurringFrom: 'ITK-6' },
      'added the next repeat in To do: Fix login',
    ],
    [
      'card.recurrence_set',
      { card, kind: 'Weekly', weekdays: [0, 3, 1] },
      'set a weekly repeat (Mon, Wed, Sun) on Fix login',
    ],
    ['card.recurrence_set', { card, kind: 'Daily' }, 'set a daily repeat on Fix login'],
    [
      'card.recurrence_set',
      { card, kind: 'Monthly', dayOfMonth: 31 },
      'set a monthly repeat (day 31) on Fix login',
    ],
    ['card.recurrence_cleared', { card }, 'stopped the repeat on Fix login'],
    [
      'card.updated',
      { card, fields: ['title'], from: 'Login bug' },
      'renamed “Login bug” to Fix login',
    ],
    [
      'card.updated',
      { card, fields: ['dueAt'], dueAt: '2026-12-01T00:00:00Z' },
      'set the due date on Fix login',
    ],
    ['card.updated', { card, fields: ['dueAt'] }, 'cleared the due date on Fix login'],
    [
      'card.updated',
      { card, fields: ['priority'], priority: 'High' },
      'set priority High on Fix login',
    ],
    ['card.updated', { card, fields: ['description'] }, 'edited the description of Fix login'],
    [
      'card.updated',
      { card, fields: ['dueAt', 'priority'] },
      'changed the due date and priority of Fix login',
    ],
    ['card.moved', { card, fromList: 'To do', toList: 'Review' }, 'moved to Review Fix login'],
    ['card.moved', { card, list: 'To do' }, 'reordered in To do Fix login'],
    ['card.archived', { card }, 'archived Fix login'],
    ['card.restored', { card }, 'restored Fix login'],
    ['card.completed', { card }, 'completed Fix login'],
    ['card.reopened', { card }, 'reopened Fix login'],
    ['card.assignees_changed', { card, added: ['Ada'], removed: [] }, 'assigned Ada to Fix login'],
    [
      'card.assignees_changed',
      { card, added: [], removed: ['Ada', 'Grace'] },
      'unassigned Ada and Grace from Fix login',
    ],
    [
      'card.assignees_changed',
      { card, added: ['Ada'], removed: ['Grace'] },
      'changed the assignees on Fix login',
    ],
    [
      'card.labels_changed',
      { card, added: ['Bug'], removed: [] },
      'added the label Bug to Fix login',
    ],
    [
      'card.labels_changed',
      { card, added: [], removed: ['Bug', 'UI'] },
      'removed the labels Bug and UI from Fix login',
    ],
    [
      'card.labels_changed',
      { card, added: ['Bug'], removed: ['UI'] },
      'changed the labels on Fix login',
    ],
    ['label.created', { label: 'Bug', emoji: '🐞' }, 'created the label 🐞 Bug'],
    ['label.updated', { label: 'Defect', from: 'Bug' }, 'renamed the label “Bug” to Defect'],
    ['label.updated', { label: 'Bug' }, 'updated the label Bug'],
    ['label.deleted', { label: 'Bug' }, 'deleted the label Bug'],
    [
      'checklist.added',
      { card, item: 'Write test' },
      'added the subtask “Write test” to Fix login',
    ],
    [
      'checklist.checked',
      { card, item: 'Write test' },
      'completed the subtask “Write test” on Fix login',
    ],
    [
      'checklist.unchecked',
      { card, item: 'Write test' },
      'reopened the subtask “Write test” on Fix login',
    ],
    [
      'checklist.updated',
      { card, item: 'Write test' },
      'edited the subtask “Write test” on Fix login',
    ],
    ['checklist.moved', { card, item: 'Write test' }, 'reordered the subtasks on Fix login'],
    [
      'checklist.deleted',
      { card, item: 'Write test' },
      'deleted the subtask “Write test” from Fix login',
    ],
    ['comment.added', { card }, 'commented on Fix login'],
    ['comment.edited', { card }, 'edited a comment on Fix login'],
    ['comment.deleted', { card }, 'deleted a comment on Fix login'],
    ['attachment.added', { card, fileName: 'spec.pdf' }, 'attached spec.pdf to Fix login'],
    ['attachment.deleted', { card, fileName: 'spec.pdf' }, 'removed spec.pdf from Fix login'],
  ])('%s → %s', (type, data, text) => {
    expect(line(type, data).text).toBe(text)
  })

  it('picks an emoji per category', () => {
    expect(line('comment.added', { card }).emoji).toBe('💬')
    expect(line('checklist.checked', { card }).emoji).toBe('☑️')
    expect(line('card.moved', { card }).emoji).toBe('📝')
  })

  it('falls back for unknown types and missing data', () => {
    expect(line('card.teleported', { card })).toEqual({ emoji: '📝', text: 'changed Fix login' })
    expect(line('robot.danced')).toEqual({ emoji: '⚡', text: 'made a change' })
    expect(line('attachment.added', { card }).text).toBe('attached a file to Fix login')
  })
})

describe('helpers', () => {
  it('joins names', () => {
    expect(joinNames([])).toBe('')
    expect(joinNames(['Ada'])).toBe('Ada')
    expect(joinNames(['Ada', 'Grace', 'Linus'])).toBe('Ada, Grace and Linus')
  })

  it('reads the category', () => {
    expect(categoryOf('member.role_changed')).toBe('member')
  })

  it('labels days relative to now', () => {
    const now = new Date(2026, 10, 18, 15, 0)
    expect(dayLabel(new Date(2026, 10, 18, 0, 5).toISOString(), now)).toBe('Today')
    expect(dayLabel(new Date(2026, 10, 17, 23, 59).toISOString(), now)).toBe('Yesterday')
    expect(dayLabel(new Date(2026, 10, 16, 12).toISOString(), now)).toBe('Mon, Nov 16')
    expect(dayLabel(new Date(2025, 10, 16, 12).toISOString(), now)).toBe('Sun, Nov 16, 2025')
  })

  it('groups newest-first entries by day, keeping order', () => {
    const now = new Date(2026, 10, 18, 15, 0)
    const at = (id: string, d: Date) => ({ id, createdAt: d.toISOString() }) as ActivityEntry
    const groups = groupByDay(
      [
        at('a', new Date(2026, 10, 18, 14)),
        at('b', new Date(2026, 10, 18, 9)),
        at('c', new Date(2026, 10, 17, 20)),
        at('d', new Date(2026, 10, 12, 8)),
      ],
      now,
    )
    expect(groups.map((g) => [g.day, g.items.map((i) => i.id)])).toEqual([
      ['Today', ['a', 'b']],
      ['Yesterday', ['c']],
      ['Thu, Nov 12', ['d']],
    ])
  })
})
