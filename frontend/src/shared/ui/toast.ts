import { readonly, ref } from 'vue'

export interface Toast {
  id: number
  text: string
  tone: 'info' | 'error'
}

const items = ref<Toast[]>([])
let nextId = 1

/** Toasts currently on screen, rendered by ToastHost. */
export const toasts = readonly(items)

export function dismissToast(id: number) {
  items.value = items.value.filter((t) => t.id !== id)
}

/** Shows a short message for `ms` milliseconds. */
export function toast(text: string, tone: Toast['tone'] = 'info', ms = 4000) {
  const id = nextId++
  items.value = [...items.value, { id, text, tone }]
  setTimeout(() => dismissToast(id), ms)
  return id
}
