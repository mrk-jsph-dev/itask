import type { LocationQuery, LocationQueryRaw } from 'vue-router'

import type { BoardList, Card, CardPriority, Label, Member } from './api'
import { priorities } from './cardOptions'

export type BoardViewKind = 'board' | 'list' | 'calendar'
export type DueFilter = 'overdue' | 'today' | 'week' | 'none'
export type SortKey = 'position' | 'due' | 'priority' | 'created' | 'title'
export type GroupKey = 'none' | 'list' | 'label' | 'assignee' | 'priority' | 'due'

export const views: readonly BoardViewKind[] = ['board', 'list', 'calendar']
export const dueFilters: readonly DueFilter[] = ['overdue', 'today', 'week', 'none']
export const sortKeys: readonly SortKey[] = ['position', 'due', 'priority', 'created', 'title']
export const groupKeys: readonly GroupKey[] = [
  'none',
  'list',
  'label',
  'assignee',
  'priority',
  'due',
]

export const dueLabels: Record<DueFilter, string> = {
  overdue: 'Overdue',
  today: 'Today',
  week: 'This week',
  none: 'No date',
}

export const sortLabels: Record<SortKey, string> = {
  position: 'Board order',
  due: 'Due date',
  priority: 'Priority',
  created: 'Newest',
  title: 'Title',
}

export const groupLabels: Record<GroupKey, string> = {
  none: 'No grouping',
  list: 'List',
  label: 'Label',
  assignee: 'Assignee',
  priority: 'Priority',
  due: 'Due date',
}

/** Assignee filter value for cards with nobody assigned (same as the API). */
export const unassigned = 'none'

/** Which cards show, shared by the Board, List and Calendar views. Empty arrays match all. */
export interface BoardFilter {
  labels: string[]
  assignees: string[]
  due: DueFilter[]
  priority: CardPriority[]
  q: string
  hideCompleted: boolean
}

/** Everything a board page keeps in the URL query. `view` is null when the URL doesn't say. */
export interface BoardState {
  view: BoardViewKind | null
  filter: BoardFilter
  sort: SortKey
  group: GroupKey
  /** Calendar month as `yyyy-mm`, or null for the current month. */
  month: string | null
}

export const emptyFilter = (): BoardFilter => ({
  labels: [],
  assignees: [],
  due: [],
  priority: [],
  q: '',
  hideCompleted: false,
})

function first(value: LocationQuery[string] | undefined): string {
  const v = Array.isArray(value) ? value[0] : value
  return typeof v === 'string' ? v : ''
}

function list(value: LocationQuery[string] | undefined): string[] {
  return [
    ...new Set(
      first(value)
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean),
    ),
  ]
}

function oneOf<T extends string>(value: string, allowed: readonly T[], fallback: T): T {
  return (allowed as readonly string[]).includes(value) ? (value as T) : fallback
}

const monthPattern = /^\d{4}-(0[1-9]|1[0-2])$/

/** Reads the board state from a route query, ignoring values it doesn't know. */
export function parseQuery(query: LocationQuery): BoardState {
  const view = first(query.view)
  const month = first(query.month)
  return {
    view: (views as readonly string[]).includes(view) ? (view as BoardViewKind) : null,
    filter: {
      labels: list(query.labels),
      assignees: list(query.assignees),
      due: list(query.due).filter((d): d is DueFilter => (dueFilters as string[]).includes(d)),
      priority: list(query.priority).filter((p): p is CardPriority =>
        (priorities as string[]).includes(p),
      ),
      q: first(query.q).slice(0, 100),
      hideCompleted: first(query.completed) === 'false',
    },
    sort: oneOf(first(query.sort), sortKeys, 'position'),
    group: oneOf(first(query.group), groupKeys, 'none'),
    month: monthPattern.test(month) ? month : null,
  }
}

/** The URL query for a board state; defaults are left out so plain board links stay plain. */
export function toQuery(state: BoardState): LocationQueryRaw {
  const { filter } = state
  const query: Record<string, string> = {}
  if (state.view && state.view !== 'board') query.view = state.view
  if (filter.labels.length) query.labels = filter.labels.join(',')
  if (filter.assignees.length) query.assignees = filter.assignees.join(',')
  if (filter.due.length) query.due = filter.due.join(',')
  if (filter.priority.length) query.priority = filter.priority.join(',')
  if (filter.q.trim()) query.q = filter.q.trim()
  if (filter.hideCompleted) query.completed = 'false'
  if (state.sort !== 'position') query.sort = state.sort
  if (state.group !== 'none') query.group = state.group
  if (state.month) query.month = state.month
  return query
}

/** Number of filter fields in use (for the toolbar badge). */
export function activeCount(filter: BoardFilter): number {
  return (
    [filter.labels, filter.assignees, filter.due, filter.priority].filter((v) => v.length).length +
    (filter.q.trim() ? 1 : 0) +
    (filter.hideCompleted ? 1 : 0)
  )
}

/** Query parameters for `GET /boards/{id}/cards`. */
export function toApiParams(
  filter: BoardFilter,
  sort: SortKey,
  range?: { from: Date; to: Date },
  timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone,
): URLSearchParams {
  const params = new URLSearchParams()
  if (filter.labels.length) params.set('labels', filter.labels.join(','))
  if (filter.assignees.length) params.set('assignees', filter.assignees.join(','))
  if (filter.due.length) params.set('due', filter.due.join(','))
  if (filter.priority.length) params.set('priority', filter.priority.join(','))
  if (filter.q.trim()) params.set('q', filter.q.trim())
  if (filter.hideCompleted) params.set('completed', 'false')
  if (sort !== 'position') params.set('sort', sort)
  if (range) {
    params.set('dueFrom', range.from.toISOString())
    params.set('dueTo', range.to.toISOString())
  }
  if (timeZone) params.set('tz', timeZone)
  return params
}

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate())
const addDays = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n)

/** Local [from, to) of today and of the rest of this week (ends after Sunday). */
export function dueRanges(now: Date) {
  const today = startOfDay(now)
  const toMonday = (8 - today.getDay()) % 7 || 7
  return {
    today: { from: today, to: addDays(today, 1) },
    week: { from: today, to: addDays(today, toMonday) },
  }
}

function inRange(date: Date, range: { from: Date; to: Date }) {
  return date >= range.from && date < range.to
}

/** Same rules as the API's due buckets, in local time. */
export function matchesDue(
  card: Pick<Card, 'dueAt' | 'completedAt'>,
  bucket: DueFilter,
  now: Date,
) {
  if (!card.dueAt) return bucket === 'none'
  const due = new Date(card.dueAt)
  if (bucket === 'overdue') return due < now && !card.completedAt
  if (bucket === 'none') return false
  return inRange(due, dueRanges(now)[bucket])
}

/** Card key typed into the text filter, e.g. `rm-12`. */
const keyPattern = /^[A-Za-z]{2,5}-\d{1,9}$/

/** Whether a card passes the filter; mirrors `GET /boards/{id}/cards`. */
export function matches(card: Card, filter: BoardFilter, now = new Date()): boolean {
  if (filter.hideCompleted && card.completedAt) return false
  if (filter.labels.length && !card.labels.some((l) => filter.labels.includes(l.id))) return false
  if (filter.assignees.length) {
    const hit =
      card.assignees.some((a) => filter.assignees.includes(a.userId)) ||
      (filter.assignees.includes(unassigned) && card.assignees.length === 0)
    if (!hit) return false
  }
  if (filter.priority.length && !filter.priority.includes(card.priority)) return false
  const q = filter.q.trim().toLowerCase()
  if (q) {
    const keyHit = keyPattern.test(q) && card.key.toLowerCase() === q
    if (!keyHit && !card.title.toLowerCase().includes(q)) return false
  }
  if (filter.due.length && !filter.due.some((b) => matchesDue(card, b, now))) return false
  return true
}

const rank = (p: CardPriority) => priorities.indexOf(p)

/**
 * Comparator for a sort key: due soonest first (no date last), priority highest first,
 * newest first, title A to Z; ties keep card number order. `position` keeps list order
 * (pass each list's position to order cards across lists).
 */
export function compareBy(sort: SortKey, listOrder: Map<string, number> = new Map()) {
  return (a: Card, b: Card): number => {
    switch (sort) {
      case 'due': {
        if (!a.dueAt || !b.dueAt)
          return (a.dueAt ? 0 : 1) - (b.dueAt ? 0 : 1) || a.number - b.number
        return Date.parse(a.dueAt) - Date.parse(b.dueAt) || a.number - b.number
      }
      case 'priority':
        return rank(b.priority) - rank(a.priority) || a.number - b.number
      case 'created':
        return b.number - a.number
      case 'title':
        return (
          a.title.localeCompare(b.title, undefined, { sensitivity: 'base' }) || a.number - b.number
        )
      default:
        return (
          (listOrder.get(a.listId) ?? 0) - (listOrder.get(b.listId) ?? 0) || a.position - b.position
        )
    }
  }
}

/** One section of the List view. */
export interface CardGroup {
  key: string
  title: string
  emoji?: string | null
  color?: string
  cards: Card[]
}

export type DueGroup = 'overdue' | 'today' | 'week' | 'later' | 'none'

/** The single due section a card falls into (first match wins). */
export function dueGroup(card: Pick<Card, 'dueAt' | 'completedAt'>, now = new Date()): DueGroup {
  if (!card.dueAt) return 'none'
  if (matchesDue(card, 'overdue', now)) return 'overdue'
  if (matchesDue(card, 'today', now)) return 'today'
  if (matchesDue(card, 'week', now)) return 'week'
  return 'later'
}

const dueGroupTitles: Record<DueGroup, string> = {
  overdue: 'Overdue',
  today: 'Today',
  week: 'This week',
  later: 'Later',
  none: 'No date',
}

interface GroupContext {
  lists: Pick<BoardList, 'id' | 'name' | 'emoji'>[]
  labels: Label[]
  members: Pick<Member, 'userId' | 'displayName'>[]
  now?: Date
}

/**
 * Splits already sorted cards into sections, keeping their order inside each. Cards with
 * several labels or assignees appear in each of those sections. Empty sections are dropped.
 */
export function groupCards(cards: Card[], group: GroupKey, ctx: GroupContext): CardGroup[] {
  const sections = (defs: Omit<CardGroup, 'cards'>[], keysOf: (c: Card) => string[]) => {
    const byKey = new Map(defs.map((d) => [d.key, { ...d, cards: [] as Card[] }]))
    for (const card of cards) {
      for (const key of keysOf(card)) byKey.get(key)?.cards.push(card)
    }
    return [...byKey.values()].filter((g) => g.cards.length)
  }

  switch (group) {
    case 'list':
      return sections(
        ctx.lists.map((l) => ({ key: l.id, title: l.name, emoji: l.emoji })),
        (c) => [c.listId],
      )
    case 'label':
      return sections(
        [
          ...ctx.labels.map((l) => ({ key: l.id, title: l.name, emoji: l.emoji, color: l.color })),
          { key: '', title: 'No label' },
        ],
        (c) => (c.labels.length ? c.labels.map((l) => l.id) : ['']),
      )
    case 'assignee': {
      const known = new Set(ctx.members.map((m) => m.userId))
      const extra = cards
        .flatMap((c) => c.assignees)
        .filter(
          (a, i, all) => !known.has(a.userId) && all.findIndex((b) => b.userId === a.userId) === i,
        )
        .map((a) => ({ userId: a.userId, displayName: a.displayName }))
      return sections(
        [
          ...[...ctx.members, ...extra].map((m) => ({ key: m.userId, title: m.displayName })),
          { key: '', title: 'Unassigned' },
        ],
        (c) => (c.assignees.length ? c.assignees.map((a) => a.userId) : ['']),
      )
    }
    case 'priority':
      return sections(
        [...priorities].reverse().map((p) => ({ key: p, title: p === 'None' ? 'No priority' : p })),
        (c) => [c.priority],
      )
    case 'due':
      return sections(
        (Object.keys(dueGroupTitles) as DueGroup[]).map((k) => ({
          key: k,
          title: dueGroupTitles[k],
        })),
        (c) => [dueGroup(c, ctx.now)],
      )
    default:
      return cards.length ? [{ key: 'all', title: 'All tasks', cards }] : []
  }
}
