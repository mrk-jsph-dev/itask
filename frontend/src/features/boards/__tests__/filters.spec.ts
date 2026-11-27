import { describe, expect, it } from 'vitest'

import type { Card } from '../api'
import {
  activeCount,
  compareBy,
  dueGroup,
  emptyFilter,
  groupCards,
  matches,
  matchesDue,
  parseQuery,
  toApiParams,
  toQuery,
  type BoardFilter,
} from '../filters'

const card = (title: string, extra: Partial<Card> = {}): Card => ({
  id: title,
  listId: 'l1',
  number: 1,
  key: `RM-1`,
  title,
  emoji: null,
  position: 1,
  dueAt: null,
  startAt: null,
  priority: 'None',
  coverColor: null,
  completedAt: null,
  archivedAt: null,
  labels: [],
  assignees: [],
  checklistDone: 0,
  checklistTotal: 0,
  commentCount: 0,
  attachmentCount: 0,
  recurring: false,
  blocked: false,
  blockerCount: 0,
  blockingCount: 0,
  rowVersion: 'rv',
  ...extra,
})

const bug = { id: 'bug', name: 'Bug', color: '#B91C1C', emoji: null }
const ui = { id: 'ui', name: 'UI', color: '#1D5BF5', emoji: '✨' }
const ada = { userId: 'ada', displayName: 'Ada', avatarColor: '#000000' }

/** Wednesday 2026-11-18, 10:00 local. */
const now = new Date(2026, 10, 18, 10, 0)
const at = (day: number, hour = 12) => new Date(2026, 10, day, hour).toISOString()
const filter = (change: Partial<BoardFilter>): BoardFilter => ({ ...emptyFilter(), ...change })

describe('board filter query', () => {
  it('round-trips through the URL and leaves defaults out', () => {
    const state = {
      view: 'list' as const,
      filter: filter({
        labels: ['bug', 'ui'],
        assignees: ['none'],
        due: ['overdue' as const],
        priority: ['High' as const],
        q: ' login ',
        hideCompleted: true,
      }),
      sort: 'due' as const,
      group: 'label' as const,
      month: '2026-12',
    }

    const query = toQuery(state)

    expect(query).toEqual({
      view: 'list',
      labels: 'bug,ui',
      assignees: 'none',
      due: 'overdue',
      priority: 'High',
      q: 'login',
      completed: 'false',
      sort: 'due',
      group: 'label',
      month: '2026-12',
    })
    expect(parseQuery(query as Record<string, string>)).toEqual({
      ...state,
      filter: { ...state.filter, q: 'login' },
    })
    expect(
      toQuery({
        view: 'board',
        filter: emptyFilter(),
        sort: 'position',
        group: 'none',
        month: null,
      }),
    ).toEqual({})
  })

  it('ignores unknown values', () => {
    const state = parseQuery({
      view: 'gantt',
      due: 'overdue,someday',
      priority: 'Huge,Low',
      sort: 'random',
      group: 'colour',
      month: '2026-13',
    })

    expect(state.view).toBeNull()
    expect(state.filter.due).toEqual(['overdue'])
    expect(state.filter.priority).toEqual(['Low'])
    expect([state.sort, state.group, state.month]).toEqual(['position', 'none', null])
  })

  it('counts active fields and builds API params', () => {
    const f = filter({ labels: ['bug', 'ui'], q: 'x', hideCompleted: true })

    const params = toApiParams(f, 'priority', undefined, 'Asia/Manila')

    expect(activeCount(f)).toBe(3)
    expect(activeCount(emptyFilter())).toBe(0)
    expect(params.toString()).toBe(
      'labels=bug%2Cui&q=x&completed=false&sort=priority&tz=Asia%2FManila',
    )
  })
})

describe('matches', () => {
  it('ORs values within a field and ANDs fields', () => {
    const a = card('Fix login', { labels: [bug], priority: 'High' })
    const b = card('Polish UI', { labels: [ui] })

    expect(matches(a, filter({ labels: ['bug', 'ui'] }), now)).toBe(true)
    expect(matches(b, filter({ labels: ['bug', 'ui'] }), now)).toBe(true)
    expect(matches(b, filter({ labels: ['bug', 'ui'], priority: ['High'] }), now)).toBe(false)
  })

  it('handles assignees, unassigned, text, keys and completed', () => {
    const mine = card('Write docs', { assignees: [ada], key: 'RM-7' })
    const nobody = card('Plan sprint', { completedAt: at(17) })

    expect(matches(mine, filter({ assignees: ['ada'] }), now)).toBe(true)
    expect(matches(nobody, filter({ assignees: ['ada'] }), now)).toBe(false)
    expect(matches(nobody, filter({ assignees: ['none'] }), now)).toBe(true)
    expect(matches(mine, filter({ q: 'DOCS' }), now)).toBe(true)
    expect(matches(mine, filter({ q: 'rm-7' }), now)).toBe(true)
    expect(matches(nobody, filter({ hideCompleted: true }), now)).toBe(false)
  })

  it('uses local due buckets', () => {
    expect(matchesDue({ dueAt: at(17), completedAt: null }, 'overdue', now)).toBe(true)
    expect(matchesDue({ dueAt: at(17), completedAt: at(17) }, 'overdue', now)).toBe(false)
    expect(matchesDue({ dueAt: at(18, 23), completedAt: null }, 'today', now)).toBe(true)
    expect(matchesDue({ dueAt: at(22, 23), completedAt: null }, 'week', now)).toBe(true)
    expect(matchesDue({ dueAt: at(23, 1), completedAt: null }, 'week', now)).toBe(false)
    expect(matchesDue({ dueAt: null, completedAt: null }, 'none', now)).toBe(true)
    expect(dueGroup({ dueAt: at(30), completedAt: null }, now)).toBe('later')
  })
})

describe('sorting and grouping', () => {
  const cards = [
    card('bravo', { number: 1, listId: 'l1', position: 1, dueAt: at(20), priority: 'Low' }),
    card('Alpha', { number: 2, listId: 'l2', position: 1, dueAt: at(19), priority: 'Urgent' }),
    card('Charlie', {
      number: 3,
      listId: 'l1',
      position: 2,
      priority: 'Medium',
      labels: [bug, ui],
    }),
  ]
  const order = (sort: Parameters<typeof compareBy>[0]) =>
    [...cards]
      .sort(
        compareBy(
          sort,
          new Map([
            ['l1', 1],
            ['l2', 2],
          ]),
        ),
      )
      .map((c) => c.title)

  it('sorts like the API', () => {
    expect(order('position')).toEqual(['bravo', 'Charlie', 'Alpha'])
    expect(order('due')).toEqual(['Alpha', 'bravo', 'Charlie'])
    expect(order('priority')).toEqual(['Alpha', 'Charlie', 'bravo'])
    expect(order('created')).toEqual(['Charlie', 'Alpha', 'bravo'])
    expect(order('title')).toEqual(['Alpha', 'bravo', 'Charlie'])
  })

  it('groups by list, label, assignee, priority and due', () => {
    const ctx = {
      lists: [
        { id: 'l1', name: 'To do', emoji: null },
        { id: 'l2', name: 'Done', emoji: null },
      ],
      labels: [bug, ui],
      members: [{ userId: 'ada', displayName: 'Ada' }],
      now,
    }
    const titles = (group: Parameters<typeof groupCards>[1]) =>
      groupCards(cards, group, ctx).map((g) => [g.title, g.cards.map((c) => c.title)])

    expect(titles('list')).toEqual([
      ['To do', ['bravo', 'Charlie']],
      ['Done', ['Alpha']],
    ])
    expect(titles('label')).toEqual([
      ['Bug', ['Charlie']],
      ['UI', ['Charlie']],
      ['No label', ['bravo', 'Alpha']],
    ])
    expect(titles('assignee')).toEqual([['Unassigned', ['bravo', 'Alpha', 'Charlie']]])
    expect(titles('priority').map((g) => g[0])).toEqual(['Urgent', 'Medium', 'Low'])
    expect(titles('due')).toEqual([
      ['This week', ['bravo', 'Alpha']],
      ['No date', ['Charlie']],
    ])
    expect(groupCards([], 'none', ctx)).toEqual([])
  })
})
