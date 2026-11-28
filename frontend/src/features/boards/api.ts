import { api } from '@/shared/api/http'

export type BoardRole = 'Viewer' | 'Editor' | 'Owner'

export const boardRoles: readonly BoardRole[] = ['Owner', 'Editor', 'Viewer']

export interface BoardSummary {
  id: string
  name: string
  emoji: string | null
  description: string | null
  color: string
  role: BoardRole
  isStarred: boolean
  memberCount: number
  archivedAt: string | null
}

export interface BoardDetail extends BoardSummary {
  keyPrefix: string
  rowVersion: string
}

export interface BoardInput {
  name: string
  emoji: string | null
  description: string | null
  color: string
  /** Card key prefix (2-5 capital letters); omitted keeps the current one. */
  keyPrefix?: string
}

export interface BoardContentInput {
  name: string
  emoji: string | null
  color?: string
  content: {
    lists: { name: string }[]
    cards: { list: string; title: string; checklist: string[] }[]
  }
}

export interface CreatedBoard {
  id: string
  name: string
  emoji: string | null
  description: string | null
  color: string
  role: BoardRole
}

export interface UpdatedBoard extends Omit<CreatedBoard, 'role'> {
  keyPrefix: string
  rowVersion: string
}

export interface Member {
  userId: string
  displayName: string
  email: string
  avatarColor: string
  role: BoardRole
  joinedAt: string
}

export interface Invite {
  id: string
  email: string | null
  role: BoardRole
  createdAt: string
  expiresAt: string
}

export interface CreatedInvite {
  id: string
  token: string
  email: string | null
  role: BoardRole
  expiresAt: string
}

const board = (id: string) => `/boards/${id}`

/** A board template in the gallery. Built-in ids are slugs, saved ones GUIDs. */
export interface BoardTemplate {
  id: string
  source: 'builtIn' | 'saved'
  name: string
  emoji: string | null
  description: string | null
  color: string
  lists: string[]
  labelCount: number
  cardCount: number
}

export interface TemplateGallery {
  builtIn: BoardTemplate[]
  mine: BoardTemplate[]
}

export const templatesApi = {
  all: () => api.get<TemplateGallery>('/templates'),
  createBoard: (templateId: string, input: BoardInput) =>
    api.post<CreatedBoard>(`/templates/${encodeURIComponent(templateId)}/boards`, input),
  /** A board from lists and card skeletons, e.g. an accepted AI draft. */
  createFromContent: (input: BoardContentInput) =>
    api.post<CreatedBoard>('/boards/from-content', input),
  save: (
    boardId: string,
    input: { name: string; description: string | null; includeCards: boolean },
  ) => api.post<BoardTemplate>(`${board(boardId)}/templates`, input),
  remove: (templateId: string) => api.del(`/templates/${templateId}`),
}

/** An open card on Home's ⏰ Due soon list. */
export interface DueSoonItem {
  id: string
  boardId: string
  boardName: string
  boardEmoji: string | null
  title: string
  emoji: string | null
  priority: CardPriority
  dueAt: string
}

/** Cards due within 7 days (overdue included), assigned to the caller or to nobody. */
export interface DueSoon {
  dueThisWeek: number
  overdue: number
  items: DueSoonItem[]
}

export const boardsApi = {
  dueSoon: () => api.get<DueSoon>('/me/due-soon'),
  mine: (filter: { archived?: boolean } = {}) =>
    api.get<BoardSummary[]>(filter.archived ? '/boards?archived=true' : '/boards'),
  get: (id: string) => api.get<BoardDetail>(board(id)),
  create: (input: BoardInput) => api.post<CreatedBoard>('/boards', input),
  update: (id: string, input: BoardInput, rowVersion: string) =>
    api.put<UpdatedBoard>(board(id), { ...input, rowVersion }),
  star: (id: string) => api.put<void>(`${board(id)}/star`),
  unstar: (id: string) => api.del(`${board(id)}/star`),
  archive: (id: string) => api.post<void>(`${board(id)}/archive`),
  restore: (id: string) => api.post<void>(`${board(id)}/restore`),
  remove: (id: string) => api.del(board(id)),

  members: (id: string) => api.get<Member[]>(`${board(id)}/members`),
  changeRole: (id: string, userId: string, role: BoardRole) =>
    api.put<void>(`${board(id)}/members/${userId}`, { role }),
  removeMember: (id: string, userId: string) => api.del(`${board(id)}/members/${userId}`),

  invites: (id: string) => api.get<Invite[]>(`${board(id)}/invites`),
  createInvite: (id: string, role: BoardRole, email: string | null) =>
    api.post<CreatedInvite>(`${board(id)}/invites`, { role, email }),
  revokeInvite: (id: string, inviteId: string) => api.del(`${board(id)}/invites/${inviteId}`),
  acceptInvite: (token: string) =>
    api.post<{ boardId: string }>(`/invites/${encodeURIComponent(token)}/accept`),
}

export interface BoardList {
  id: string
  name: string
  emoji: string | null
  position: number
  wipLimit: number | null
  archivedAt: string | null
  /** Active cards in order; empty for archived lists. */
  cards: Card[]
}

export interface ListInput {
  name: string
  emoji: string | null
  wipLimit: number | null
}

/** Neighbours a moved list ends up between; ids of the lists left and right of it. */
export interface ListNeighbours {
  beforeId: string | null
  afterId: string | null
}

const lists = (boardId: string) => `${board(boardId)}/lists`

export const listsApi = {
  all: (boardId: string) => api.get<BoardList[]>(lists(boardId)),
  archived: (boardId: string) => api.get<BoardList[]>(`${lists(boardId)}?archived=true`),
  create: (boardId: string, input: ListInput) =>
    api.post<Omit<BoardList, 'archivedAt' | 'cards'>>(lists(boardId), input),
  rename: (boardId: string, listId: string, input: ListInput) =>
    api.put<Omit<BoardList, 'archivedAt' | 'cards'>>(`${lists(boardId)}/${listId}`, input),
  move: (boardId: string, listId: string, neighbours: ListNeighbours) =>
    api.put<{ id: string; position: number }>(`${lists(boardId)}/${listId}/position`, neighbours),
  archive: (boardId: string, listId: string) =>
    api.post<void>(`${lists(boardId)}/${listId}/archive`),
  restore: (boardId: string, listId: string) =>
    api.post<void>(`${lists(boardId)}/${listId}/restore`),
}

export type CardPriority = 'None' | 'Low' | 'Medium' | 'High' | 'Urgent'

export interface Label {
  id: string
  name: string
  color: string
  emoji: string | null
}

export type LabelInput = Omit<Label, 'id'>

export interface Assignee {
  userId: string
  displayName: string
  avatarColor: string
}

/** A card as a tile shows it. `rowVersion` goes back with moves. */
export interface Card {
  id: string
  listId: string
  number: number
  key: string
  title: string
  emoji: string | null
  position: number
  dueAt: string | null
  startAt: string | null
  priority: CardPriority
  coverColor: string | null
  completedAt: string | null
  archivedAt: string | null
  labels: Label[]
  assignees: Assignee[]
  checklistDone: number
  checklistTotal: number
  commentCount: number
  /** Confirmed uploads only. */
  attachmentCount: number
  /** The card carries a repeat rule. */
  recurring: boolean
  /** At least one card it waits on is still open. */
  blocked: boolean
  /** Open cards this one waits on. */
  blockerCount: number
  /** Open cards waiting on this one. */
  blockingCount: number
  rowVersion: string
}

/** A card another card waits on, as the card sheet lists it. */
export interface Blocker {
  id: string
  key: string
  title: string
  completed: boolean
  archived: boolean
}

export interface ChecklistItem {
  id: string
  text: string
  isDone: boolean
  position: number
  dueAt: string | null
  assigneeId: string | null
}

/** Fields to change; absent ones stay, `null` clears. */
export interface ChecklistPatch {
  text?: string
  dueAt?: string | null
  assigneeId?: string | null
}

export interface Attachment {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedById: string
  uploadedByName: string | null
  createdAt: string
}

export interface Comment {
  id: string
  authorId: string
  authorName: string
  authorColor: string
  body: string
  createdAt: string
  editedAt: string | null
  mentions: { userId: string; displayName: string }[]
}

export interface CommentPage {
  items: Comment[]
  nextCursor: string | null
}

export interface UploadSlot {
  blobName: string
  uploadUrl: string
  expiresAt: string
}

export interface CardDetail {
  card: Card
  description: string | null
  createdById: string
  createdByName: string | null
  createdAt: string
  checklist: ChecklistItem[]
  attachments: Attachment[]
  recurrence: Recurrence | null
  blockedBy: Blocker[]
}

export type RecurrenceKind = 'Daily' | 'Weekly' | 'Monthly'

/** What `PUT .../recurrence` takes. Weekdays are 0 (Sunday) to 6. */
export interface RecurrenceInput {
  kind: RecurrenceKind
  weekdays: number[]
  dayOfMonth: number | null
  targetListId: string
}

/** A card's repeat rule. `nextRunAt` is when the next copy appears. */
export interface Recurrence extends RecurrenceInput {
  nextRunAt: string
}

/** Fields to change; absent ones stay, `null` clears. */
export interface CardPatch {
  title?: string
  description?: string | null
  dueAt?: string | null
  startAt?: string | null
  priority?: CardPriority
  coverColor?: string | null
  emoji?: string | null
}

/** Target list and the ids of the cards the moved card ends up between. */
export interface CardMove extends ListNeighbours {
  listId: string
}

const cards = (boardId: string) => `${board(boardId)}/cards`

/** Cards matching a filter, from `GET /boards/{id}/cards`. */
export interface CardQueryResult {
  items: Card[]
  /** More cards matched than the API returns at once. */
  truncated: boolean
}

export const cardsApi = {
  query: (boardId: string, params: URLSearchParams) =>
    api.get<CardQueryResult>(`${cards(boardId)}?${params.toString()}`),
  create: (boardId: string, listId: string, title: string) =>
    api.post<Card>(`${lists(boardId)}/${listId}/cards`, { title }),
  get: (boardId: string, cardId: string) => api.get<CardDetail>(`${cards(boardId)}/${cardId}`),
  update: (boardId: string, cardId: string, patch: CardPatch) =>
    api.patch<Card>(`${cards(boardId)}/${cardId}`, patch),
  /** `overrideDoorway` (Owners) moves the card through a block-mode doorway anyway. */
  move: (boardId: string, cardId: string, move: CardMove, rowVersion: string, overrideDoorway = false) =>
    api.put<{ card: Card; renumbered: boolean }>(`${cards(boardId)}/${cardId}/position`, {
      ...move,
      rowVersion,
      ...(overrideDoorway ? { overrideDoorway } : {}),
    }),
  complete: (boardId: string, cardId: string) =>
    api.post<Card>(`${cards(boardId)}/${cardId}/complete`),
  reopen: (boardId: string, cardId: string) => api.post<Card>(`${cards(boardId)}/${cardId}/reopen`),
  archive: (boardId: string, cardId: string) =>
    api.post<Card>(`${cards(boardId)}/${cardId}/archive`),
  restore: (boardId: string, cardId: string) =>
    api.post<Card>(`${cards(boardId)}/${cardId}/restore`),
  setLabels: (boardId: string, cardId: string, labelIds: string[]) =>
    api.put<Card>(`${cards(boardId)}/${cardId}/labels`, { labelIds }),
  setAssignees: (boardId: string, cardId: string, userIds: string[]) =>
    api.put<Card>(`${cards(boardId)}/${cardId}/assignees`, { userIds }),
  setRecurrence: (boardId: string, cardId: string, input: RecurrenceInput) =>
    api.put<Recurrence>(`${cards(boardId)}/${cardId}/recurrence`, input),
  clearRecurrence: (boardId: string, cardId: string) =>
    api.del(`${cards(boardId)}/${cardId}/recurrence`),
  /** 409 `dependency_cycle` when the blocker already waits on this card. */
  addBlocker: (boardId: string, cardId: string, blockerId: string) =>
    api.put<Card>(`${cards(boardId)}/${cardId}/blocked-by/${blockerId}`),
  removeBlocker: (boardId: string, cardId: string, blockerId: string) =>
    api.del<Card>(`${cards(boardId)}/${cardId}/blocked-by/${blockerId}`),
}

const card = (boardId: string, cardId: string) => `${cards(boardId)}/${cardId}`

export const checklistApi = {
  add: (boardId: string, cardId: string, text: string) =>
    api.post<ChecklistItem>(`${card(boardId, cardId)}/checklist`, { text }),
  update: (boardId: string, cardId: string, itemId: string, patch: ChecklistPatch) =>
    api.patch<ChecklistItem>(`${card(boardId, cardId)}/checklist/${itemId}`, patch),
  setDone: (boardId: string, cardId: string, itemId: string, isDone: boolean) =>
    api.put<ChecklistItem>(`${card(boardId, cardId)}/checklist/${itemId}/done`, { isDone }),
  move: (boardId: string, cardId: string, itemId: string, neighbours: ListNeighbours) =>
    api.put<{ item: ChecklistItem; renumbered: boolean }>(
      `${card(boardId, cardId)}/checklist/${itemId}/position`,
      neighbours,
    ),
  remove: (boardId: string, cardId: string, itemId: string) =>
    api.del(`${card(boardId, cardId)}/checklist/${itemId}`),
}

export const commentsApi = {
  page: (boardId: string, cardId: string, cursor: string | null = null) =>
    api.get<CommentPage>(
      `${card(boardId, cardId)}/comments${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`,
    ),
  add: (boardId: string, cardId: string, body: string) =>
    api.post<Comment>(`${card(boardId, cardId)}/comments`, { body }),
  edit: (boardId: string, cardId: string, commentId: string, body: string) =>
    api.put<Comment>(`${card(boardId, cardId)}/comments/${commentId}`, { body }),
  remove: (boardId: string, cardId: string, commentId: string) =>
    api.del(`${card(boardId, cardId)}/comments/${commentId}`),
}

export const attachmentsApi = {
  requestUpload: (
    boardId: string,
    cardId: string,
    file: { fileName: string; contentType: string; sizeBytes: number },
  ) => api.post<UploadSlot>(`${card(boardId, cardId)}/attachments/upload-url`, file),
  confirm: (boardId: string, cardId: string, blobName: string, fileName: string) =>
    api.post<Attachment>(`${card(boardId, cardId)}/attachments`, { blobName, fileName }),
  downloadUrl: (boardId: string, cardId: string, attachmentId: string) =>
    api.get<{ url: string; expiresAt: string }>(
      `${card(boardId, cardId)}/attachments/${attachmentId}/url`,
    ),
  remove: (boardId: string, cardId: string, attachmentId: string) =>
    api.del(`${card(boardId, cardId)}/attachments/${attachmentId}`),
}

const labels = (boardId: string) => `${board(boardId)}/labels`

export const labelsApi = {
  all: (boardId: string) => api.get<Label[]>(labels(boardId)),
  create: (boardId: string, input: LabelInput) => api.post<Label>(labels(boardId), input),
  update: (boardId: string, labelId: string, input: LabelInput) =>
    api.put<Label>(`${labels(boardId)}/${labelId}`, input),
  remove: (boardId: string, labelId: string) => api.del(`${labels(boardId)}/${labelId}`),
}
