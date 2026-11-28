import type {
  ActionSpec,
  Automation,
  AutomationInput,
  ConditionSpec,
  Priority,
  TriggerSpec,
} from './api'

/** The board's lists, labels and members that automations refer to. */
export interface BoardRefs {
  lists: { id: string; name: string }[]
  labels: { id: string; name: string }[]
  members: { userId: string; displayName: string; role: string }[]
}

export interface Option {
  value: string
  label: string
}

export const priorities: Priority[] = ['Low', 'Medium', 'High', 'Urgent']

/** Triggers in the order the editor offers them; `hourly` ones run in the hourly job. */
export const triggerOptions: (Option & {
  hourly?: boolean
  needs?: 'list' | 'label' | 'days' | 'list+days'
})[] = [
  { value: 'card.entered', label: 'a card enters', needs: 'list' },
  { value: 'card.left', label: 'a card leaves', needs: 'list' },
  { value: 'card.created', label: 'a card is created' },
  { value: 'card.completed', label: 'a card is completed' },
  { value: 'card.reopened', label: 'a card is reopened' },
  { value: 'label.added', label: 'a label is added', needs: 'label' },
  { value: 'label.removed', label: 'a label is removed', needs: 'label' },
  { value: 'assignees.changed', label: 'assignees change' },
  { value: 'checklist.done', label: 'the checklist is finished' },
  { value: 'comment.added', label: 'someone comments' },
  { value: 'card.unblocked', label: 'a card is unblocked' },
  { value: 'due.soon', label: 'a due date is close', needs: 'days', hourly: true },
  { value: 'due.overdue', label: 'a card is overdue', hourly: true },
  { value: 'list.stale', label: 'a card sits in', needs: 'list+days', hourly: true },
  { value: 'completed.aged', label: 'a card was completed', needs: 'days', hourly: true },
]

export const conditionOptions: Option[] = [
  { value: 'assignee.set', label: 'has an assignee' },
  { value: 'assignee.none', label: 'has nobody assigned' },
  { value: 'assignee.is', label: 'is assigned to' },
  { value: 'label.any', label: 'has the label' },
  { value: 'label.none', label: "doesn't have the label" },
  { value: 'priority.atLeast', label: 'has priority at least' },
  { value: 'due.set', label: 'has a due date' },
  { value: 'checklist.done', label: 'has a finished checklist' },
  { value: 'blocked.no', label: 'has no open blockers' },
  { value: 'blocked.yes', label: 'waits on an open card' },
]

export const actionOptions: Option[] = [
  { value: 'move', label: 'move it to' },
  { value: 'complete', label: 'mark it complete' },
  { value: 'reopen', label: 'reopen it' },
  { value: 'label.add', label: 'add the label' },
  { value: 'label.remove', label: 'remove the label' },
  { value: 'assign', label: 'assign' },
  { value: 'unassign', label: 'unassign everyone' },
  { value: 'priority', label: 'set priority' },
  { value: 'due', label: 'set the due date' },
  { value: 'checklist.add', label: 'add checklist items' },
  { value: 'comment', label: 'comment' },
  { value: 'notify', label: 'notify' },
  { value: 'archive', label: 'archive it' },
]

/** ✨ AI actions: instant rules only, one per rule, charged to the rule creator's AI quota. */
export const aiActionOptions: Option[] = [
  { value: 'ai.breakdown', label: '✨ break it down into a checklist' },
  { value: 'ai.triage', label: '✨ pick labels and priority' },
  { value: 'ai.summary', label: '✨ add a summary comment' },
]

export const isAiAction = (type: string) => type.startsWith('ai.')

/** True when the automation spends AI quota when it runs. */
export const usesAi = (a: Pick<Automation, 'actions'>) => a.actions.some((x) => isAiAction(x.type))

/** Requirements a fix-mode doorway can't fill in by itself. */
export const unfixable = new Set(['due.set', 'checklist.done'])

/** Requirements a fix-mode doorway can't fill in either, so it only warns about them. */
export const warnOnlyInFix = new Set(['blocked.no'])

export const isHourly = (trigger: string) =>
  triggerOptions.some((t) => t.value === trigger && t.hourly)

const nameOf = (
  items: { id: string; name: string }[],
  id: string | null | undefined,
  fallback: string,
) => items.find((i) => i.id === id)?.name ?? fallback

const memberName = (refs: BoardRefs, id: string | null | undefined) =>
  refs.members.find((m) => m.userId === id)?.displayName ?? 'a former member'

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`

export function describeTrigger(t: TriggerSpec, refs: BoardRefs): string {
  const list = nameOf(refs.lists, t.listId, 'a list')
  const label = nameOf(refs.labels, t.labelId, 'a label')
  const days = t.days ?? 1
  switch (t.type) {
    case 'card.entered':
      return `a card enters ${list}`
    case 'card.left':
      return `a card leaves ${list}`
    case 'card.created':
      return t.listId ? `a card is created in ${list}` : 'a card is created'
    case 'label.added':
      return `${label} is added`
    case 'label.removed':
      return `${label} is removed`
    case 'comment.added':
      return t.mentions ? 'someone is mentioned in a comment' : 'someone comments'
    case 'due.soon':
      return `a card is due within ${plural(days, 'day')}`
    case 'list.stale':
      return `a card sits in ${list} for ${plural(days, 'day')}`
    case 'completed.aged':
      return `a card was completed ${plural(days, 'day')} ago`
    default:
      return triggerOptions.find((o) => o.value === t.type)?.label ?? t.type
  }
}

/** A requirement or filter as a noun phrase: "an assignee", "the label Bug". */
export function describeCondition(c: ConditionSpec, refs: BoardRefs): string {
  const labels = (c.labelIds ?? [])
    .map((id) => nameOf(refs.labels, id, 'a deleted label'))
    .join(' or ')
  switch (c.type) {
    case 'assignee.set':
      return 'an assignee'
    case 'assignee.none':
      return 'nobody assigned'
    case 'assignee.is':
      return `${memberName(refs, c.userId)} assigned`
    case 'label.any':
      return `the label ${labels}`
    case 'label.none':
      return `no label ${labels}`
    case 'priority.atLeast':
      return `priority ${c.priority ?? 'High'} or higher`
    case 'due.set':
      return 'a due date'
    case 'checklist.done':
      return 'a finished checklist'
    case 'blocked.no':
      return 'no open blockers'
    case 'blocked.yes':
      return 'an open blocker'
    default:
      return c.type
  }
}

export function describeAction(a: ActionSpec, refs: BoardRefs): string {
  switch (a.type) {
    case 'move':
      return `move it to ${a.position === 'top' ? 'the top of ' : ''}${nameOf(refs.lists, a.listId, 'a list')}`
    case 'label.add':
      return `add ${nameOf(refs.labels, a.labelId, 'a label')}`
    case 'label.remove':
      return `remove ${nameOf(refs.labels, a.labelId, 'a label')}`
    case 'assign':
      return a.who === 'actor'
        ? 'assign whoever did it'
        : a.who === 'creator'
          ? 'assign the creator'
          : `assign ${memberName(refs, a.userId)}`
    case 'priority':
      return `set priority ${a.priority ?? 'High'}`
    case 'due':
      return a.days ? `set due in ${plural(a.days, 'day')}` : 'set due today'
    case 'checklist.add':
      return `add ${plural(a.items?.length ?? 0, 'checklist item')}`
    case 'comment':
      return `comment “${a.text ?? ''}”`
    case 'notify':
      return a.who === 'owners'
        ? 'notify the owners'
        : a.who === 'members'
          ? 'notify the board'
          : 'notify the assignees'
    default:
      return [...actionOptions, ...aiActionOptions].find((o) => o.value === a.type)?.label ?? a.type
  }
}

/** One-line description, e.g. "When a card is completed, move it to Done." */
export function describe(
  a: Pick<
    Automation,
    'kind' | 'listId' | 'direction' | 'mode' | 'trigger' | 'conditions' | 'actions'
  >,
  refs: BoardRefs,
): string {
  const actions = a.actions.map((x) => describeAction(x, refs)).join(', ')
  if (a.kind === 'Doorway') {
    const list = nameOf(refs.lists, a.listId, 'this list')
    const way = a.direction === 'Leave' ? `Leaving ${list}` : `Coming into ${list}`
    const needs = a.conditions.map((c) => describeCondition(c, refs)).join(' and ')
    const otherwise =
      a.mode === 'Block'
        ? 'Otherwise: blocked.'
        : a.mode === 'Fix'
          ? 'Otherwise: filled in.'
          : 'Otherwise: a warning.'
    const parts = [needs ? `${way} needs ${needs}. ${otherwise}` : `${way}:`]
    if (actions) parts.push(`${needs ? 'Then ' : ''}${actions}.`)
    return parts.join(' ')
  }
  const ifs = a.conditions.length
    ? ` and it has ${a.conditions.map((c) => describeCondition(c, refs)).join(' and ')}`
    : ''
  return `When ${describeTrigger(a.trigger, refs)}${ifs}, ${actions}.`
}

/** Requirements of `doorway` the card doesn't meet, as phrases (for the warning chip). */
export function unmetRequirements(
  card: {
    assignees: { userId: string }[]
    labels: { id: string }[]
    priority: Priority
    dueAt: string | null
    checklistDone: number
    checklistTotal: number
    blocked?: boolean
  },
  doorway: Pick<Automation, 'conditions'>,
  refs: BoardRefs,
): string[] {
  const order: Priority[] = ['None', 'Low', 'Medium', 'High', 'Urgent']
  const meets = (c: ConditionSpec) => {
    switch (c.type) {
      case 'assignee.set':
        return card.assignees.length > 0
      case 'assignee.none':
        return card.assignees.length === 0
      case 'assignee.is':
        return card.assignees.some((a) => a.userId === c.userId)
      case 'label.any':
        return card.labels.some((l) => c.labelIds?.includes(l.id))
      case 'label.none':
        return !card.labels.some((l) => c.labelIds?.includes(l.id))
      case 'priority.atLeast':
        return order.indexOf(card.priority) >= order.indexOf(c.priority ?? 'None')
      case 'due.set':
        return card.dueAt !== null
      case 'checklist.done':
        return card.checklistTotal > 0 && card.checklistDone === card.checklistTotal
      case 'blocked.no':
        return !card.blocked
      case 'blocked.yes':
        return !!card.blocked
      default:
        return true
    }
  }
  return doorway.conditions.filter((c) => !meets(c)).map((c) => describeCondition(c, refs))
}

/** A ready-made automation, offered when the board has the lists and labels it names. */
export interface Preset {
  id: string
  emoji: string
  title: string
  build: (refs: BoardRefs) => AutomationInput | null
}

const list = (refs: BoardRefs, ...names: string[]) =>
  refs.lists.find((l) => names.some((n) => l.name.toLowerCase() === n.toLowerCase()))?.id ?? null
const label = (refs: BoardRefs, ...names: string[]) =>
  refs.labels.find((l) => names.some((n) => l.name.toLowerCase() === n.toLowerCase()))?.id ?? null

const rule = (
  name: string,
  trigger: TriggerSpec,
  actions: ActionSpec[],
  conditions: ConditionSpec[] = [],
): AutomationInput => ({
  kind: 'Rule',
  name,
  listId: null,
  direction: null,
  mode: null,
  trigger,
  conditions,
  actions,
})

const DONE = ['Done', 'Completed', 'Shipped']
const REVIEW = ['Review', 'In review', 'QA']
const DOING = ['In progress', 'Doing', 'Working on it']

export const presets: Preset[] = [
  {
    id: 'review-gate',
    emoji: '🚪',
    title: 'Review gate: cards need an owner and a finished checklist',
    build: (refs) => {
      const review = list(refs, ...REVIEW)
      return review
        ? {
            kind: 'Doorway',
            name: 'Review gate',
            listId: review,
            direction: 'Enter',
            mode: 'Block',
            trigger: null,
            conditions: [{ type: 'assignee.set' }, { type: 'checklist.done' }],
            actions: [],
          }
        : null
    },
  },
  {
    id: 'start-owner',
    emoji: '🙋',
    title: 'Starting a card makes you its owner',
    build: (refs) => {
      const doing = list(refs, ...DOING)
      return doing
        ? {
            kind: 'Doorway',
            name: 'Start owner',
            listId: doing,
            direction: 'Enter',
            mode: 'Fix',
            trigger: null,
            conditions: [{ type: 'assignee.set' }],
            actions: [],
          }
        : null
    },
  },
  {
    id: 'done-completes',
    emoji: '✅',
    title: 'Cards moved to Done are marked complete',
    build: (refs) => {
      const done = list(refs, ...DONE)
      return done
        ? rule('Done completes', { type: 'card.entered', listId: done }, [{ type: 'complete' }])
        : null
    },
  },
  {
    id: 'finish-flow',
    emoji: '🏁',
    title: 'Completed cards move to Done',
    build: (refs) => {
      const done = list(refs, ...DONE)
      return done
        ? rule('Finish flow', { type: 'card.completed' }, [{ type: 'move', listId: done }])
        : null
    },
  },
  {
    id: 'reopen-from-done',
    emoji: '🔁',
    title: 'Cards taken out of Done are reopened',
    build: (refs) => {
      const done = list(refs, ...DONE)
      return done
        ? {
            kind: 'Doorway',
            name: 'Back from Done',
            listId: done,
            direction: 'Leave',
            mode: 'Warn',
            trigger: null,
            conditions: [],
            actions: [{ type: 'reopen' }],
          }
        : null
    },
  },
  {
    id: 'checklist-review',
    emoji: '☑️',
    title: 'A finished checklist sends the card to Review',
    build: (refs) => {
      const review = list(refs, ...REVIEW)
      return review
        ? rule('Checklist to review', { type: 'checklist.done' }, [
            { type: 'move', listId: review },
          ])
        : null
    },
  },
  {
    id: 'bug-priority',
    emoji: '🐛',
    title: 'Bugs get High priority',
    build: (refs) => {
      const bug = label(refs, 'Bug')
      return bug
        ? rule('Bug priority', { type: 'label.added', labelId: bug }, [
            { type: 'priority', priority: 'High' },
          ])
        : null
    },
  },
  {
    id: 'new-due',
    emoji: '📅',
    title: 'New cards are due in a week',
    build: () => rule('Due in a week', { type: 'card.created' }, [{ type: 'due', days: 7 }]),
  },
  {
    id: 'due-soon',
    emoji: '⏰',
    title: 'Nudge assignees two days before the due date',
    build: () =>
      rule('Due soon nudge', { type: 'due.soon', days: 2 }, [
        { type: 'notify', who: 'assignees', text: 'Due in two days.' },
      ]),
  },
  {
    id: 'overdue',
    emoji: '🔥',
    title: 'Overdue cards become Urgent',
    build: () =>
      rule('Overdue escalation', { type: 'due.overdue' }, [
        { type: 'priority', priority: 'Urgent' },
        { type: 'notify', who: 'assignees' },
      ]),
  },
  {
    id: 'stuck-review',
    emoji: '🧊',
    title: 'Tell the owners when a card waits in Review for 3 days',
    build: (refs) => {
      const review = list(refs, ...REVIEW)
      return review
        ? rule('Stuck in review', { type: 'list.stale', listId: review, days: 3 }, [
            { type: 'notify', who: 'owners' },
          ])
        : null
    },
  },
  {
    id: 'tidy-done',
    emoji: '🧹',
    title: 'Archive cards two weeks after they are completed',
    build: () => rule('Tidy up', { type: 'completed.aged', days: 14 }, [{ type: 'archive' }]),
  },
]

/** Presets that fit the board, skipping ones it already has (same name). */
export function presetsFor(refs: BoardRefs, existing: Pick<Automation, 'name'>[]) {
  const names = new Set(existing.map((a) => a.name.toLowerCase()))
  return presets
    .map((p) => ({ preset: p, input: p.build(refs) }))
    .filter(
      (p): p is { preset: Preset; input: AutomationInput } =>
        !!p.input && !names.has(p.input.name.toLowerCase()),
    )
}

/** A deep copy of plain automation data; also works on Vue reactive proxies, which structuredClone rejects. */
export function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

/** A new, empty rule for the editor. */
export function blankRule(): AutomationInput {
  return rule('', { type: 'card.completed' }, [{ type: 'complete' }])
}
