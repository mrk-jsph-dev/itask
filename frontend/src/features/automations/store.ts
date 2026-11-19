import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { ApiError } from '@/shared/api/http'
import { automationsApi, type Automation, type AutomationInput } from './api'

/** A move a block-mode doorway refused, shown until the user dismisses it. */
export interface DoorwayRefusal {
  cardId: string
  listId: string
  index: number
  message: string
  rule: string
  unmet: string[]
}

/** The doorway refusal inside an API error, or null for any other error. */
export function doorwayRefusal(
  e: unknown,
): Omit<DoorwayRefusal, 'cardId' | 'listId' | 'index'> | null {
  if (!(e instanceof ApiError) || e.status !== 409 || e.extensions.code !== 'doorway') return null
  const unmet = Array.isArray(e.extensions.unmet)
    ? e.extensions.unmet.filter((u): u is string => typeof u === 'string')
    : []
  return { message: e.message, rule: String(e.extensions.rule ?? ''), unmet }
}

/** Automations of the open board. Viewers only get its doorways. */
export const useAutomationsStore = defineStore('automations', () => {
  const boardId = ref<string | null>(null)
  const items = ref<Automation[] | null>(null)
  const refused = ref<DoorwayRefusal | null>(null)

  const doorways = computed(() =>
    (items.value ?? []).filter((a) => a.kind === 'Doorway' && a.enabled && !a.problem),
  )

  function doorwaysFor(listId: string) {
    return doorways.value.filter((a) => a.listId === listId)
  }

  async function load(id: string) {
    if (boardId.value !== id) items.value = null
    boardId.value = id
    const loaded = await automationsApi.all(id)
    if (boardId.value === id) items.value = loaded
  }

  async function refresh() {
    if (boardId.value) await load(boardId.value)
  }

  function replace(saved: Automation) {
    const list = items.value ?? []
    items.value = list.some((a) => a.id === saved.id)
      ? list.map((a) => (a.id === saved.id ? saved : a))
      : [...list, saved]
  }

  async function create(input: AutomationInput) {
    const saved = await automationsApi.create(boardId.value!, input)
    replace(saved)
    return saved
  }

  async function update(id: string, input: AutomationInput) {
    const current = items.value?.find((a) => a.id === id)
    if (!current) return
    const { kind: _kind, ...rest } = input
    replace(await automationsApi.update(boardId.value!, id, rest, current.rowVersion))
  }

  async function toggle(id: string, enabled: boolean) {
    replace(await automationsApi.toggle(boardId.value!, id, enabled))
  }

  async function remove(id: string) {
    await automationsApi.remove(boardId.value!, id)
    items.value = (items.value ?? []).filter((a) => a.id !== id)
  }

  function reset() {
    boardId.value = null
    items.value = null
    refused.value = null
  }

  return {
    boardId,
    items,
    doorways,
    refused,
    doorwaysFor,
    load,
    refresh,
    create,
    update,
    toggle,
    remove,
    reset,
  }
})
