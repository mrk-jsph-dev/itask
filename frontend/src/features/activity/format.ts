import type { ActivityCategory, ActivityEntry } from './api'

/** One feed row: `{actor} {what} {target}`, e.g. "Ada · moved to Done · Fix login". */
export interface ActivityLine {
  emoji: string
  what: string
  /** Card title, list or label name; null when the sentence is complete. */
  target: string | null
}

export interface ActivityChip {
  label: string
  category: ActivityCategory | null
}

/** Filter chips from the ActivityC frame. */
export const activityChips: readonly ActivityChip[] = [
  { label: 'All', category: null },
  { label: 'Cards', category: 'card' },
  { label: 'Comments', category: 'comment' },
  { label: 'Tasks', category: 'checklist' },
]

const emojis: Record<string, string> = {
  board: '🚀',
  member: '👋',
  list: '📋',
  card: '📝',
  label: '🏷️',
  checklist: '☑️',
  comment: '💬',
  attachment: '📎',
}

const fieldNames: Record<string, string> = {
  title: 'title',
  description: 'description',
  dueAt: 'due date',
  startAt: 'start date',
  priority: 'priority',
  coverColor: 'cover',
  name: 'name',
  emoji: 'emoji',
  color: 'colour',
  keyPrefix: 'card key',
}

export function categoryOf(type: string): string {
  return type.split('.')[0] ?? type
}

const str = (value: unknown): string | null => (typeof value === 'string' ? value : null)
const strings = (value: unknown): string[] =>
  Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : []
const quoted = (value: unknown) => `“${str(value) ?? ''}”`

/** "Ada", "Ada and Grace", "Ada, Grace and Linus". */
export function joinNames(names: string[]): string {
  if (names.length <= 1) return names[0] ?? ''
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

function cardTitle(data: Record<string, unknown>): string | null {
  const card = data.card as { title?: unknown } | undefined
  return str(card?.title)
}

/** "assigned Ada to" / "unassigned Ada from" / "changed the assignees on". */
function setChange(
  data: Record<string, unknown>,
  add: (names: string, count: number) => string,
  remove: (names: string, count: number) => string,
  mixed: string,
) {
  const added = strings(data.added)
  const removed = strings(data.removed)
  if (added.length && !removed.length) return `${add(joinNames(added), added.length)} to`
  if (removed.length && !added.length) return `${remove(joinNames(removed), removed.length)} from`
  return mixed
}

const weekdayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

/** "a daily repeat", "a weekly repeat (Mon, Wed)", "a monthly repeat (day 31)". */
function repeatText(data: Record<string, unknown>): string {
  const days = Array.isArray(data.weekdays)
    ? [...(data.weekdays as unknown[])]
        .filter((d): d is number => typeof d === 'number')
        .sort((a, b) => ((a + 6) % 7) - ((b + 6) % 7))
        .map((d) => weekdayNames[d])
    : []
  switch (data.kind) {
    case 'Weekly':
      return days.length ? `a weekly repeat (${days.join(', ')})` : 'a weekly repeat'
    case 'Monthly':
      return typeof data.dayOfMonth === 'number'
        ? `a monthly repeat (day ${data.dayOfMonth})`
        : 'a monthly repeat'
    case 'Daily':
      return 'a daily repeat'
    default:
      return 'a repeat'
  }
}

const labelNoun = (count: number) => (count === 1 ? 'label' : 'labels')

function cardUpdate(data: Record<string, unknown>): string {
  const fields = strings(data.fields)
  if (fields.length === 1) {
    switch (fields[0]) {
      case 'title':
        return `renamed ${quoted(data.from)} to`
      case 'dueAt':
        return data.dueAt ? 'set the due date on' : 'cleared the due date on'
      case 'priority':
        return `set priority ${str(data.priority) ?? ''} on`
      case 'description':
        return 'edited the description of'
    }
  }
  const names = fields.map((f) => fieldNames[f] ?? f)
  return names.length ? `changed the ${joinNames(names)} of` : 'updated'
}

function sentence(type: string, data: Record<string, unknown>): [string, string | null] {
  const card = cardTitle(data)
  const list = str(data.list)
  const label = [str(data.emoji), str(data.label)].filter(Boolean).join(' ')
  const member = str((data.member as { name?: unknown } | undefined)?.name)

  switch (type) {
    case 'board.created':
      return [
        data.template
          ? `created the board from the ${str(data.template)} template`
          : 'created the board',
        null,
      ]
    case 'board.updated':
      return data.from
        ? [`renamed the board from ${quoted(data.from)} to`, str(data.name)]
        : [
            `changed the board ${joinNames(strings(data.fields).map((f) => fieldNames[f] ?? f))}`,
            null,
          ]
    case 'board.archived':
      return ['archived the board', null]
    case 'board.restored':
      return ['restored the board', null]

    case 'member.joined':
      return [data.role ? `joined as ${str(data.role)}` : 'joined the board', null]
    case 'member.left':
      return ['left the board', null]
    case 'member.removed':
      return ['removed', member]
    case 'member.role_changed':
      return [`made ${member ?? 'a member'} ${str(data.to) ?? ''}`.trim(), null]

    case 'list.created':
      return ['added the list', list]
    case 'list.renamed':
      return [`renamed the list ${quoted(data.from)} to`, list]
    case 'list.updated':
      return ['updated the list', list]
    case 'list.moved':
      return ['reordered the list', list]
    case 'list.archived':
      return ['archived the list', list]
    case 'list.restored':
      return ['restored the list', list]

    case 'card.created':
      if (data.recurringFrom) return [`added the next repeat in ${list ?? 'its list'}:`, card]
      return [list ? `created in ${list}` : 'created', card]
    case 'card.recurrence_set':
      return [`set ${repeatText(data)} on`, card]
    case 'card.recurrence_cleared':
      return ['stopped the repeat on', card]
    case 'card.updated':
      return [cardUpdate(data), card]
    case 'card.moved':
      return [
        data.fromList ? `moved to ${str(data.toList)}` : `reordered in ${list ?? 'its list'}`,
        card,
      ]
    case 'card.archived':
      return ['archived', card]
    case 'card.restored':
      return ['restored', card]
    case 'card.completed':
      return ['completed', card]
    case 'card.reopened':
      return ['reopened', card]
    case 'card.assignees_changed':
      return [
        setChange(
          data,
          (n) => `assigned ${n}`,
          (n) => `unassigned ${n}`,
          'changed the assignees on',
        ),
        card,
      ]
    case 'card.labels_changed':
      return [
        setChange(
          data,
          (n, count) => `added the ${labelNoun(count)} ${n}`,
          (n, count) => `removed the ${labelNoun(count)} ${n}`,
          'changed the labels on',
        ),
        card,
      ]

    case 'label.created':
      return ['created the label', label]
    case 'label.updated':
      return [data.from ? `renamed the label ${quoted(data.from)} to` : 'updated the label', label]
    case 'label.deleted':
      return ['deleted the label', label]

    case 'checklist.added':
      return [`added the subtask ${quoted(data.item)} to`, card]
    case 'checklist.checked':
      return [`completed the subtask ${quoted(data.item)} on`, card]
    case 'checklist.unchecked':
      return [`reopened the subtask ${quoted(data.item)} on`, card]
    case 'checklist.updated':
      return [`edited the subtask ${quoted(data.item)} on`, card]
    case 'checklist.moved':
      return ['reordered the subtasks on', card]
    case 'checklist.deleted':
      return [`deleted the subtask ${quoted(data.item)} from`, card]

    case 'comment.added':
      return ['commented on', card]
    case 'comment.edited':
      return ['edited a comment on', card]
    case 'comment.deleted':
      return ['deleted a comment on', card]

    case 'attachment.added':
      return [`attached ${str(data.fileName) ?? 'a file'} to`, card]
    case 'attachment.deleted':
      return [`removed ${str(data.fileName) ?? 'a file'} from`, card]

    default:
      return card ? ['changed', card] : ['made a change', null]
  }
}

/** Builds the readable line for an entry; unknown types fall back to a generic sentence. */
export function describeActivity(entry: Pick<ActivityEntry, 'type' | 'data'>): ActivityLine {
  const [what, target] = sentence(entry.type, entry.data ?? {})
  return { emoji: emojis[categoryOf(entry.type)] ?? '⚡', what, target: target || null }
}

export { dayLabel, groupByDay, timeLabel, type DayGroup } from '@/shared/ui/dates'
