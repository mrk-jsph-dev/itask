import { readonly, ref } from 'vue'

export interface ToastAction {
  label: string
  run: () => void
}

export interface Toast {
  id: number
  text: string
  tone: 'info' | 'error'
  action?: ToastAction
}

const items = ref<Toast[]>([])
let nextId = 1

/** Toasts currently on screen, rendered by ToastHost. */
export const toasts = readonly(items)

export function dismissToast(id: number) {
  items.value = items.value.filter((t) => t.id !== id)
}

/** Shows a short message for `ms` milliseconds. */
export function toast(text: string, tone: Toast['tone'] = 'info', ms = 4000, action?: ToastAction) {
  const id = nextId++
  items.value = [...items.value, { id, text, tone, action }]
  setTimeout(() => dismissToast(id), ms)
  return id
}

/** Shows an info toast with an Undo button for 5 s, the pattern for destructive actions. */
export function toastWithUndo(text: string, undo: () => void) {
  return toast(text, 'info', 5000, { label: 'Undo', run: undo })
}

/** Runs a toast's action once and closes it. */
export function runToastAction(id: number) {
  const item = items.value.find((t) => t.id === id)
  dismissToast(id)
  item?.action?.run()
}
