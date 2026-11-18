import { nextTick, onBeforeUnmount, onMounted, ref, type Ref } from 'vue'

const items = (menu: HTMLElement | null) => [
  ...(menu?.querySelectorAll<HTMLButtonElement>('[role^="menuitem"]:not(:disabled)') ?? []),
]

/**
 * State and keyboard handling for a `role="menu"` popup opened by a trigger button:
 * focus moves to the first item on open, arrows cycle, Escape closes back to the trigger,
 * and clicks outside close it.
 */
export function useMenu(
  menu: Readonly<Ref<HTMLElement | null>>,
  trigger: Readonly<Ref<HTMLElement | null>>,
) {
  const open = ref(false)

  function close(focusTrigger = false) {
    open.value = false
    if (focusTrigger) trigger.value?.focus()
  }

  async function focusFirst() {
    await nextTick()
    items(menu.value)[0]?.focus()
  }

  async function toggle() {
    open.value = !open.value
    if (open.value) await focusFirst()
  }

  function onKeydown(event: KeyboardEvent) {
    const all = items(menu.value)
    const index = all.indexOf(document.activeElement as HTMLButtonElement)
    if (event.key === 'ArrowDown') all[(index + 1) % all.length]?.focus()
    else if (event.key === 'ArrowUp') all[(index - 1 + all.length) % all.length]?.focus()
    else if (event.key === 'Escape') close(true)
    else return
    event.preventDefault()
    event.stopPropagation()
  }

  function closeOnOutside(event: MouseEvent) {
    const target = event.target as Node
    if (open.value && !menu.value?.contains(target) && !trigger.value?.contains(target)) close()
  }

  onMounted(() => document.addEventListener('click', closeOnOutside))
  onBeforeUnmount(() => document.removeEventListener('click', closeOnOutside))

  return { open, close, toggle, focusFirst, onKeydown }
}
