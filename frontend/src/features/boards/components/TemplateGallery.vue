<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { toast, toastWithUndo } from '@/shared/ui/toast'
import { templatesApi, type BoardTemplate, type TemplateGallery } from '../api'
import { errorText } from '../store'

/** Selected template id; null is a blank board. */
const selected = defineModel<string | null>({ required: true })
const emit = defineEmits<{ pick: [template: BoardTemplate | null] }>()

const gallery = ref<TemplateGallery | null>(null)
const failed = ref(false)
/** Saved templates hidden while their Undo toast is up. */
const hidden = ref<string[]>([])

const mine = computed(() => (gallery.value?.mine ?? []).filter((t) => !hidden.value.includes(t.id)))

onMounted(async () => {
  try {
    gallery.value = await templatesApi.all()
  } catch {
    failed.value = true
  }
})

function pick(template: BoardTemplate | null) {
  selected.value = template?.id ?? null
  emit('pick', template)
}

function summary(t: BoardTemplate) {
  const lists = `${t.lists.length} ${t.lists.length === 1 ? 'list' : 'lists'}`
  return t.cardCount ? `${lists} · ${t.cardCount} ${t.cardCount === 1 ? 'card' : 'cards'}` : lists
}

/** Hides the template at once and deletes it after the 5 s Undo window. */
function remove(template: BoardTemplate) {
  if (selected.value === template.id) pick(null)
  hidden.value = [...hidden.value, template.id]
  let undone = false
  const restore = () => (hidden.value = hidden.value.filter((id) => id !== template.id))
  toastWithUndo(`Deleted template “${template.name}”.`, () => {
    undone = true
    restore()
  })
  setTimeout(() => {
    if (undone) return
    templatesApi.remove(template.id).catch((e: unknown) => {
      restore()
      toast(errorText(e, "Couldn't delete the template."), 'error')
    })
  }, 5000)
}

const tile =
  'press flex h-full w-full flex-col gap-1 rounded-[16px] border p-3 text-left transition-colors'
const tileState = (active: boolean) =>
  active ? 'border-accent bg-[#EFF4FF] ring-1 ring-accent' : 'border-line hover:bg-hover'
</script>

<template>
  <fieldset class="flex flex-col gap-2">
    <legend class="mb-1.5 text-[13px] font-semibold">Start from</legend>
    <div
      v-if="!gallery && !failed"
      class="grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-2"
      aria-busy="true"
    >
      <SkeletonBlock v-for="i in 6" :key="i" height="84px" />
    </div>
    <template v-else>
      <div
        role="radiogroup"
        aria-label="Templates"
        class="grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-2"
      >
        <button
          type="button"
          role="radio"
          :aria-checked="selected === null"
          class="fade-up"
          :class="[tile, tileState(selected === null)]"
          style="--i: 0"
          @click="pick(null)"
        >
          <span class="text-xl" aria-hidden="true">✨</span>
          <span class="font-semibold">Blank board</span>
          <span class="text-xs text-muted">To do · In progress · Review · Done</span>
        </button>
        <button
          v-for="(template, i) in gallery?.builtIn ?? []"
          :key="template.id"
          type="button"
          role="radio"
          :aria-checked="selected === template.id"
          :title="template.description ?? undefined"
          class="fade-up"
          :class="[tile, tileState(selected === template.id)]"
          :style="{ '--i': i + 1 }"
          @click="pick(template)"
        >
          <span class="text-xl" aria-hidden="true">{{ template.emoji }}</span>
          <span class="font-semibold">{{ template.name }}</span>
          <span class="text-xs text-muted">{{ summary(template) }}</span>
        </button>
      </div>
      <p v-if="failed" class="text-sm text-muted">
        Templates couldn't load. You can still start a blank board.
      </p>

      <template v-if="mine.length">
        <p class="mt-2 text-[13px] font-semibold">Your templates</p>
        <div
          role="radiogroup"
          aria-label="Your templates"
          class="grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-2"
        >
          <div v-for="template in mine" :key="template.id" class="relative">
            <button
              type="button"
              role="radio"
              :aria-checked="selected === template.id"
              :class="[tile, tileState(selected === template.id)]"
              @click="pick(template)"
            >
              <span class="text-xl" aria-hidden="true">{{ template.emoji ?? '📋' }}</span>
              <span class="pr-6 font-semibold break-words">{{ template.name }}</span>
              <span class="text-xs text-muted">{{ summary(template) }}</span>
            </button>
            <button
              type="button"
              class="ghost absolute top-2 right-2 flex h-7 w-7 items-center justify-center rounded-full text-muted"
              :aria-label="`Delete template ${template.name}`"
              @click="remove(template)"
            >
              ✕
            </button>
          </div>
        </div>
      </template>
    </template>
  </fieldset>
</template>
