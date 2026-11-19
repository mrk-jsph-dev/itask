import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import {
  boardsApi,
  templatesApi,
  type BoardContentInput,
  type BoardDetail,
  type BoardInput,
  type BoardRole,
  type BoardSummary,
  type CreatedBoard,
  type Invite,
  type Member,
} from './api'
import { byName } from './options'

export function errorText(e: unknown, fallback = 'Something went wrong.'): string {
  if (e instanceof ApiError && e.errors) return Object.values(e.errors).flat().join(' ')
  if (e instanceof ApiError && e.status < 500) return e.message
  return fallback
}

export const useBoardsStore = defineStore('boards', () => {
  /** Boards on the home page; null while loading. */
  const boards = ref<BoardSummary[] | null>(null)
  const showArchived = ref(false)
  /** Active boards for the sidebar, kept apart from `boards` so the Archived view doesn't replace them. */
  const navBoards = ref<BoardSummary[] | null>(null)
  /** The New board dialog, opened from Home or the sidebar. */
  const creating = ref(false)

  /** The board open on the board page, with its members and pending invites. */
  const current = ref<BoardDetail | null>(null)
  const members = ref<Member[]>([])
  const invites = ref<Invite[]>([])

  const starred = computed(() => (boards.value ?? []).filter((b) => b.isStarred))
  const isOwner = computed(() => current.value?.role === 'Owner')

  async function load(archived = showArchived.value) {
    showArchived.value = archived
    boards.value = null
    boards.value = await boardsApi.mine({ archived })
    if (!archived) navBoards.value = boards.value
  }

  async function loadNav() {
    navBoards.value = await boardsApi.mine()
  }

  /** Creates a blank board, or one from a template when `templateId` is given. */
  async function create(input: BoardInput, templateId: string | null = null) {
    return add(
      await (templateId ? templatesApi.createBoard(templateId, input) : boardsApi.create(input)),
    )
  }

  async function createFromContent(input: BoardContentInput) {
    return add(await templatesApi.createFromContent(input))
  }

  function add(created: CreatedBoard) {
    const summary: BoardSummary = { ...created, isStarred: false, memberCount: 1, archivedAt: null }
    if (boards.value && !showArchived.value) {
      boards.value = [...boards.value, summary].sort(byName)
    }
    if (navBoards.value) navBoards.value = [...navBoards.value, summary].sort(byName)
    return created
  }

  function patchBoard(id: string, changes: Partial<BoardSummary>) {
    const patch = (list: BoardSummary[]) =>
      list.map((b) => (b.id === id ? { ...b, ...changes } : b))
    if (boards.value) boards.value = patch(boards.value)
    if (navBoards.value) navBoards.value = patch(navBoards.value)
    if (current.value?.id === id) current.value = { ...current.value, ...changes }
  }

  function dropBoard(id: string) {
    if (boards.value) boards.value = boards.value.filter((b) => b.id !== id)
    if (navBoards.value) navBoards.value = navBoards.value.filter((b) => b.id !== id)
  }

  /** Flips the star at once and rolls back with a toast if the request fails. */
  async function toggleStar(id: string, isStarred: boolean) {
    patchBoard(id, { isStarred: !isStarred })
    try {
      await (isStarred ? boardsApi.unstar(id) : boardsApi.star(id))
    } catch (e) {
      patchBoard(id, { isStarred })
      toast(errorText(e, "Couldn't update the star."), 'error')
    }
  }

  async function open(id: string) {
    current.value = null
    members.value = []
    invites.value = []
    current.value = await boardsApi.get(id)
    members.value = await boardsApi.members(id)
    if (current.value.role === 'Owner') invites.value = await boardsApi.invites(id)
  }

  /** Reloads the open board and its members without the loading state, e.g. after a live event. */
  async function refreshCurrent() {
    const id = current.value?.id
    if (!id) return
    const [board, people] = await Promise.all([boardsApi.get(id), boardsApi.members(id)])
    if (current.value?.id !== id) return
    current.value = board
    members.value = people
    invites.value = board.role === 'Owner' ? await boardsApi.invites(id) : []
    patchBoard(id, { name: board.name, emoji: board.emoji, color: board.color })
  }

  async function saveDetails(input: BoardInput) {
    const board = current.value!
    const saved = await boardsApi.update(board.id, input, board.rowVersion)
    const { rowVersion, ...details } = saved
    current.value = { ...board, ...details, rowVersion }
    patchBoard(board.id, details)
  }

  async function archive(id: string) {
    await boardsApi.archive(id)
    dropBoard(id)
    if (current.value?.id === id)
      current.value = { ...current.value, archivedAt: new Date().toISOString() }
  }

  async function restore(id: string) {
    await boardsApi.restore(id)
    dropBoard(id)
    void loadNav().catch(() => {})
    if (current.value?.id === id) current.value = { ...current.value, archivedAt: null }
  }

  async function remove(id: string) {
    await boardsApi.remove(id)
    dropBoard(id)
    if (current.value?.id === id) current.value = null
  }

  async function changeRole(userId: string, role: BoardRole) {
    const board = current.value!
    await boardsApi.changeRole(board.id, userId, role)
    members.value = members.value.map((m) => (m.userId === userId ? { ...m, role } : m))
  }

  async function removeMember(userId: string) {
    const board = current.value!
    await boardsApi.removeMember(board.id, userId)
    members.value = members.value.filter((m) => m.userId !== userId)
    current.value = { ...board, memberCount: members.value.length }
  }

  /** Leaving drops the board from every list; the caller navigates away. */
  async function leave(boardId: string, userId: string) {
    await boardsApi.removeMember(boardId, userId)
    dropBoard(boardId)
    if (current.value?.id === boardId) current.value = null
  }

  async function createInvite(role: BoardRole, email: string | null) {
    const board = current.value!
    const created = await boardsApi.createInvite(board.id, role, email)
    const pending: Invite = {
      id: created.id,
      email: created.email,
      role: created.role,
      expiresAt: created.expiresAt,
      createdAt: new Date().toISOString(),
    }
    invites.value = [pending, ...invites.value]
    return created
  }

  async function revokeInvite(inviteId: string) {
    await boardsApi.revokeInvite(current.value!.id, inviteId)
    invites.value = invites.value.filter((i) => i.id !== inviteId)
  }

  return {
    boards,
    showArchived,
    navBoards,
    creating,
    loadNav,
    starred,
    current,
    members,
    invites,
    isOwner,
    load,
    create,
    createFromContent,
    toggleStar,
    open,
    refreshCurrent,
    saveDetails,
    archive,
    restore,
    remove,
    changeRole,
    removeMember,
    leave,
    createInvite,
    revokeInvite,
  }
})
