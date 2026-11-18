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
  rowVersion: string
}

export interface BoardInput {
  name: string
  emoji: string | null
  description: string | null
  color: string
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

export const boardsApi = {
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
    api.post<Omit<BoardList, 'archivedAt'>>(lists(boardId), input),
  rename: (boardId: string, listId: string, input: ListInput) =>
    api.put<Omit<BoardList, 'archivedAt'>>(`${lists(boardId)}/${listId}`, input),
  move: (boardId: string, listId: string, neighbours: ListNeighbours) =>
    api.put<{ id: string; position: number }>(`${lists(boardId)}/${listId}/position`, neighbours),
  archive: (boardId: string, listId: string) =>
    api.post<void>(`${lists(boardId)}/${listId}/archive`),
  restore: (boardId: string, listId: string) =>
    api.post<void>(`${lists(boardId)}/${listId}/restore`),
}
