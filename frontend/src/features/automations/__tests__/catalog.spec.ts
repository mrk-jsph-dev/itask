import { describe, expect, it } from 'vitest'

import type { Automation } from '../api'
import {
  describe as describeAutomation,
  isHourly,
  presetsFor,
  unmetRequirements,
  type BoardRefs,
} from '../catalog'

const refs: BoardRefs = {
  lists: [
    { id: 'todo', name: 'To do' },
    { id: 'doing', name: 'In progress' },
    { id: 'review', name: 'Review' },
    { id: 'done', name: 'Done' },
  ],
  labels: [{ id: 'bug', name: 'Bug' }],
  members: [{ userId: 'u1', displayName: 'Ada', role: 'Owner' }],
}

const base = { listId: null, direction: null, mode: null, conditions: [], actions: [] }

describe('automation sentences', () => {
  it('describes a rule with its condition and actions', () => {
    expect(
      describeAutomation(
        {
          ...base,
          kind: 'Rule',
          trigger: { type: 'card.completed' },
          conditions: [{ type: 'label.any', labelIds: ['bug'] }],
          actions: [
            { type: 'move', listId: 'done' },
            { type: 'priority', priority: 'High' },
          ],
        },
        refs,
      ),
    ).toBe('When a card is completed and it has the label Bug, move it to Done, set priority High.')
  })

  it('describes a doorway with its mode', () => {
    expect(
      describeAutomation(
        {
          ...base,
          kind: 'Doorway',
          listId: 'review',
          direction: 'Enter',
          mode: 'Block',
          trigger: { type: 'card.entered', listId: 'review' },
          conditions: [{ type: 'assignee.set' }, { type: 'checklist.done' }],
        },
        refs,
      ),
    ).toBe('Coming into Review needs an assignee and a finished checklist. Otherwise: blocked.')
  })

  it('names hourly triggers', () => {
    expect(isHourly('due.soon')).toBe(true)
    expect(isHourly('card.completed')).toBe(false)
  })
})

describe('doorway requirements on a card', () => {
  const card = {
    assignees: [],
    labels: [{ id: 'bug' }],
    priority: 'Low' as const,
    dueAt: null,
    checklistDone: 1,
    checklistTotal: 2,
  }

  it('lists what the card is missing', () => {
    const doorway = {
      conditions: [
        { type: 'assignee.set' },
        { type: 'label.any', labelIds: ['bug'] },
        { type: 'priority.atLeast', priority: 'High' as const },
        { type: 'checklist.done' },
      ],
    }
    expect(unmetRequirements(card, doorway, refs)).toEqual([
      'an assignee',
      'priority High or higher',
      'a finished checklist',
    ])
  })
})

describe('presets', () => {
  it('offers only presets the board has lists and labels for, and not twice', () => {
    const noReview: BoardRefs = {
      ...refs,
      lists: refs.lists.filter((l) => l.id !== 'review'),
      labels: [],
    }
    const ids = presetsFor(noReview, []).map((p) => p.preset.id)
    expect(ids).not.toContain('review-gate')
    expect(ids).not.toContain('bug-priority')
    expect(ids).toContain('finish-flow')

    const existing = [{ name: 'Finish flow' }] as Pick<Automation, 'name'>[]
    expect(presetsFor(refs, existing).map((p) => p.preset.id)).not.toContain('finish-flow')
  })

  it('builds presets against the board ids', () => {
    const gate = presetsFor(refs, []).find((p) => p.preset.id === 'review-gate')!
    expect(gate.input).toMatchObject({ kind: 'Doorway', listId: 'review', mode: 'Block' })
  })
})
