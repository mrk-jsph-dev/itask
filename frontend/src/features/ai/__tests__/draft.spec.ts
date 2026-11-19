import { describe, expect, it } from 'vitest'

import { goalBoardContent, quickAddCard } from '../draft'

describe('quickAddCard', () => {
  const lists = [{ id: 'todo' }, { id: 'review' }]
  const labels = [{ id: 'bug' }]

  it('keeps the named list and only existing labels', () => {
    const card = quickAddCard(
      {
        title: 'Fix login',
        dueDate: '2030-01-04',
        priority: 'High',
        labelIds: ['bug', 'gone'],
        listId: 'review',
        remaining: 3,
      },
      lists,
      labels,
    )
    expect(card).toEqual({
      listId: 'review',
      title: 'Fix login',
      dueDate: '2030-01-04',
      priority: 'High',
      labelIds: ['bug'],
    })
  })

  it('falls back to the first list', () => {
    const draft = {
      title: 'x',
      dueDate: null,
      priority: 'None' as const,
      labelIds: [],
      remaining: 1,
    }
    expect(quickAddCard({ ...draft, listId: null }, lists, labels).listId).toBe('todo')
    expect(quickAddCard({ ...draft, listId: 'archived' }, lists, labels).listId).toBe('todo')
  })
})

describe('goalBoardContent', () => {
  it('follows renamed lists, drops blank or repeated lists and their cards', () => {
    const content = goalBoardContent(
      { name: ' Portfolio ', emoji: '🎨' },
      ['Plan', ' ', 'Ship it', 'plan'],
      [
        { listIndex: 0, title: 'Pick a domain', checklist: ['Compare'] },
        { listIndex: 1, title: 'Lost', checklist: [] },
        { listIndex: 2, title: ' Go live ', checklist: [] },
        { listIndex: 3, title: 'Duplicate list', checklist: [] },
        { listIndex: 0, title: '  ', checklist: [] },
      ],
    )
    expect(content).toEqual({
      name: 'Portfolio',
      emoji: '🎨',
      content: {
        lists: [{ name: 'Plan' }, { name: 'Ship it' }],
        cards: [
          { list: 'Plan', title: 'Pick a domain', checklist: ['Compare'] },
          { list: 'Ship it', title: 'Go live', checklist: [] },
        ],
      },
    })
  })
})
