import type { AiPriority, GoalBoardDraft, QuickAddDraft } from './api'

export const aiPriorities: AiPriority[] = ['None', 'Low', 'Medium', 'High', 'Urgent']

/** The card a quick-add draft becomes once the user accepts it. */
export interface QuickAddCard {
  listId: string
  title: string
  dueDate: string | null
  priority: AiPriority
  labelIds: string[]
}

/** Fills a quick-add draft's gaps: the first list when none was named, only labels that still exist. */
export function quickAddCard(
  draft: QuickAddDraft,
  lists: readonly { id: string }[],
  labels: readonly { id: string }[],
): QuickAddCard {
  const listId =
    draft.listId && lists.some((l) => l.id === draft.listId) ? draft.listId : (lists[0]?.id ?? '')
  return {
    listId,
    title: draft.title,
    dueDate: draft.dueDate,
    priority: draft.priority,
    labelIds: draft.labelIds.filter((id) => labels.some((l) => l.id === id)),
  }
}

/** Board content for `POST /boards/from-content` from an edited goal draft. */
export interface GoalBoardContent {
  name: string
  emoji: string | null
  content: {
    lists: { name: string }[]
    cards: { list: string; title: string; checklist: string[] }[]
  }
}

/**
 * Turns the edited draft into board content: blank and repeated list names drop out, cards
 * follow their list (by index, so renaming a list keeps its cards), blank titles drop out.
 */
export function goalBoardContent(
  draft: Pick<GoalBoardDraft, 'name' | 'emoji'>,
  lists: readonly string[],
  cards: readonly { listIndex: number; title: string; checklist: string[] }[],
): GoalBoardContent {
  const names: string[] = []
  const nameAt = new Map<number, string>()
  lists.forEach((raw, i) => {
    const name = raw.trim()
    if (!name || names.some((n) => n.toLowerCase() === name.toLowerCase())) return
    names.push(name)
    nameAt.set(i, name)
  })
  return {
    name: draft.name.trim(),
    emoji: draft.emoji,
    content: {
      lists: names.map((name) => ({ name })),
      cards: cards
        .filter((c) => nameAt.has(c.listIndex) && c.title.trim())
        .map((c) => ({
          list: nameAt.get(c.listIndex)!,
          title: c.title.trim(),
          checklist: c.checklist,
        })),
    },
  }
}
