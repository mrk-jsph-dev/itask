import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { toast, toastWithUndo } from '@/shared/ui/toast'
import {
  attachmentsApi,
  cardsApi,
  checklistApi,
  commentsApi,
  type Attachment,
  type ChecklistItem,
  type ChecklistPatch,
  type Comment,
  type Recurrence,
  type RecurrenceInput,
} from './api'
import { useCardsStore } from './cardsStore'
import { moveItem, neighbours } from './ordering'
import { errorText } from './store'
import { uploadFile, uploadProblem } from './upload'

/** How long a removed item stays recoverable before the server deletes it. */
const undoMs = 5000

/**
 * The card open in the detail sheet: description, checklist, files and comments. Checklist
 * and comment counts are copied to the board's card tile as they change.
 */
export const useCardDetailStore = defineStore('cardDetail', () => {
  const cards = useCardsStore()

  const boardId = ref<string | null>(null)
  const cardId = ref<string | null>(null)
  const loading = ref(false)
  const description = ref('')
  /** The card's repeat rule, or null when it doesn't repeat. */
  const recurrence = ref<Recurrence | null>(null)
  const checklist = ref<ChecklistItem[]>([])
  const attachments = ref<Attachment[]>([])
  /** Oldest first, as the thread shows them. */
  const comments = ref<Comment[]>([])
  const nextCursor = ref<string | null>(null)
  /** Files being uploaded, by name. */
  const uploading = ref<string[]>([])

  const done = computed(() => checklist.value.filter((i) => i.isDone).length)
  const progress = computed(() =>
    checklist.value.length ? Math.round((done.value / checklist.value.length) * 100) : 0,
  )

  const ids = () => [boardId.value!, cardId.value!] as const
  const isOpen = (b: string, c: string) => boardId.value === b && cardId.value === c

  function syncChecklistCount() {
    if (cardId.value)
      cards.patch(cardId.value, {
        checklistDone: done.value,
        checklistTotal: checklist.value.length,
      })
  }

  /** Adds to a card tile's counts; works whether or not the card is still open. */
  function bumpTile(c: string, delta: { comments?: number; items?: number; done?: number }) {
    const spot = cards.find(c)
    if (!spot) return
    const tile = spot.list.cards[spot.index]!
    cards.patch(c, {
      commentCount: Math.max(0, tile.commentCount + (delta.comments ?? 0)),
      checklistTotal: Math.max(0, tile.checklistTotal + (delta.items ?? 0)),
      checklistDone: Math.max(0, tile.checklistDone + (delta.done ?? 0)),
    })
  }

  async function open(b: string, c: string) {
    boardId.value = b
    cardId.value = c
    loading.value = true
    description.value = ''
    recurrence.value = null
    checklist.value = []
    attachments.value = []
    comments.value = []
    nextCursor.value = null
    try {
      const [detail, page] = await Promise.all([cardsApi.get(b, c), commentsApi.page(b, c)])
      if (!isOpen(b, c)) return
      description.value = detail.description ?? ''
      recurrence.value = detail.recurrence ?? null
      checklist.value = detail.checklist
      attachments.value = detail.attachments
      comments.value = [...page.items].reverse()
      nextCursor.value = page.nextCursor
    } finally {
      if (isOpen(b, c)) loading.value = false
    }
  }

  function close() {
    boardId.value = null
    cardId.value = null
  }

  /**
   * Hides something at once and deletes it on the server once the Undo toast has gone.
   * `restore` puts it back when the user undoes or the delete fails.
   */
  function removeWithUndo(
    text: string,
    hide: () => void,
    restore: () => void,
    commit: () => Promise<unknown>,
  ) {
    hide()
    let undone = false
    toastWithUndo(text, () => {
      undone = true
      restore()
    })
    setTimeout(() => {
      if (undone) return
      commit().catch((e: unknown) => {
        restore()
        toast(errorText(e, "Couldn't delete it."), 'error')
      })
    }, undoMs)
  }

  // ---- checklist ----

  async function addItem(text: string) {
    const [b, c] = ids()
    const item = await checklistApi.add(b, c, text)
    if (!isOpen(b, c)) return
    checklist.value = [...checklist.value, item]
    syncChecklistCount()
  }

  function replaceItem(item: ChecklistItem) {
    checklist.value = checklist.value.map((i) => (i.id === item.id ? item : i))
  }

  /** Optimistic: flips at once, rolls back with a toast if the server refuses. */
  async function toggleItem(itemId: string) {
    const [b, c] = ids()
    const item = checklist.value.find((i) => i.id === itemId)
    if (!item) return
    replaceItem({ ...item, isDone: !item.isDone })
    syncChecklistCount()
    try {
      await checklistApi.setDone(b, c, itemId, !item.isDone)
    } catch (e) {
      if (!isOpen(b, c)) return
      replaceItem(item)
      syncChecklistCount()
      toast(errorText(e, "Couldn't update the item."), 'error')
    }
  }

  async function updateItem(itemId: string, patch: ChecklistPatch) {
    const [b, c] = ids()
    const saved = await checklistApi.update(b, c, itemId, patch)
    if (isOpen(b, c)) replaceItem(saved)
  }

  /** Moves the item at `from` to `to` (already clamped), optimistically. */
  async function moveChecklistItem(from: number, to: number) {
    if (from === to) return
    const [b, c] = ids()
    const before = checklist.value
    const reordered = moveItem(before, from, to)
    checklist.value = reordered
    await commitItemOrder(b, c, reordered, to, before)
  }

  /** Saves an order the drag already applied; `before` is the order to restore on error. */
  async function commitDraggedItem(index: number, before: ChecklistItem[]) {
    const [b, c] = ids()
    await commitItemOrder(b, c, checklist.value, index, before)
  }

  async function commitItemOrder(
    b: string,
    c: string,
    items: ChecklistItem[],
    index: number,
    before: ChecklistItem[],
  ) {
    try {
      const { item, renumbered } = await checklistApi.move(
        b,
        c,
        items[index]!.id,
        neighbours(items, index),
      )
      if (!isOpen(b, c)) return
      if (renumbered) checklist.value = (await cardsApi.get(b, c)).checklist
      else replaceItem(item)
    } catch (e) {
      if (!isOpen(b, c)) return
      checklist.value = before
      toast(errorText(e, "Couldn't move the item."), 'error')
    }
  }

  function removeItem(itemId: string) {
    const [b, c] = ids()
    const index = checklist.value.findIndex((i) => i.id === itemId)
    const item = checklist.value[index]
    if (!item) return
    removeWithUndo(
      `Deleted "${item.text}".`,
      () => {
        checklist.value = checklist.value.filter((i) => i.id !== itemId)
        syncChecklistCount()
      },
      () => {
        if (!isOpen(b, c)) {
          bumpTile(c, { items: 1, done: item.isDone ? 1 : 0 })
          return
        }
        const items = [...checklist.value]
        items.splice(Math.min(index, items.length), 0, item)
        checklist.value = items
        syncChecklistCount()
      },
      () => checklistApi.remove(b, c, itemId),
    )
  }

  // ---- attachments ----

  /** Uploads each allowed file; problems show as toasts. */
  async function upload(files: readonly File[]) {
    const [b, c] = ids()
    await Promise.all(
      files.map(async (file) => {
        const problem = uploadProblem(file)
        if (problem) {
          toast(problem, 'error')
          return
        }
        uploading.value = [...uploading.value, file.name]
        try {
          const attachment = await uploadFile(b, c, file)
          if (isOpen(b, c)) attachments.value = [...attachments.value, attachment]
        } catch (e) {
          toast(errorText(e, `Couldn't upload ${file.name}.`), 'error')
        } finally {
          uploading.value = uploading.value.filter((n) => n !== file.name)
        }
      }),
    )
  }

  /** Opens a short-lived download link. */
  async function download(attachmentId: string) {
    const [b, c] = ids()
    try {
      const { url } = await attachmentsApi.downloadUrl(b, c, attachmentId)
      window.open(url, '_blank', 'noopener')
    } catch (e) {
      toast(errorText(e, "Couldn't download the file."), 'error')
    }
  }

  function removeAttachment(attachmentId: string) {
    const [b, c] = ids()
    const index = attachments.value.findIndex((a) => a.id === attachmentId)
    const attachment = attachments.value[index]
    if (!attachment) return
    removeWithUndo(
      `Deleted ${attachment.fileName}.`,
      () => (attachments.value = attachments.value.filter((a) => a.id !== attachmentId)),
      () => {
        if (!isOpen(b, c)) return
        const items = [...attachments.value]
        items.splice(Math.min(index, items.length), 0, attachment)
        attachments.value = items
      },
      () => attachmentsApi.remove(b, c, attachmentId),
    )
  }

  // ---- comments ----

  async function loadOlder() {
    const [b, c] = ids()
    if (!nextCursor.value) return
    const page = await commentsApi.page(b, c, nextCursor.value)
    if (!isOpen(b, c)) return
    comments.value = [...[...page.items].reverse(), ...comments.value]
    nextCursor.value = page.nextCursor
  }

  async function addComment(body: string) {
    const [b, c] = ids()
    const comment = await commentsApi.add(b, c, body)
    if (!isOpen(b, c)) return
    comments.value = [...comments.value, comment]
    bumpTile(c, { comments: 1 })
  }

  async function editComment(commentId: string, body: string) {
    const [b, c] = ids()
    const saved = await commentsApi.edit(b, c, commentId, body)
    if (isOpen(b, c)) comments.value = comments.value.map((x) => (x.id === commentId ? saved : x))
  }

  function removeComment(commentId: string) {
    const [b, c] = ids()
    const index = comments.value.findIndex((x) => x.id === commentId)
    const comment = comments.value[index]
    if (!comment) return
    removeWithUndo(
      'Comment deleted.',
      () => {
        comments.value = comments.value.filter((x) => x.id !== commentId)
        bumpTile(c, { comments: -1 })
      },
      () => {
        bumpTile(c, { comments: 1 })
        if (!isOpen(b, c)) return
        const items = [...comments.value]
        items.splice(Math.min(index, items.length), 0, comment)
        comments.value = items
      },
      () => commentsApi.remove(b, c, commentId),
    )
  }

  async function setRecurrence(input: RecurrenceInput) {
    const [b, c] = ids()
    const saved = await cardsApi.setRecurrence(b, c, input)
    if (isOpen(b, c)) recurrence.value = saved
    cards.patch(c, { recurring: true })
  }

  async function clearRecurrence() {
    const [b, c] = ids()
    await cardsApi.clearRecurrence(b, c)
    if (isOpen(b, c)) recurrence.value = null
    cards.patch(c, { recurring: false })
  }

  return {
    cardId,
    loading,
    description,
    recurrence,
    setRecurrence,
    clearRecurrence,
    checklist,
    attachments,
    comments,
    nextCursor,
    uploading,
    done,
    progress,
    open,
    close,
    addItem,
    toggleItem,
    updateItem,
    moveChecklistItem,
    commitDraggedItem,
    removeItem,
    upload,
    download,
    removeAttachment,
    loadOlder,
    addComment,
    editComment,
    removeComment,
  }
})
