<script setup lang="ts">
import { ref, useId } from 'vue'

import { colorPalette } from '@/shared/ui/palette'
import { toast } from '@/shared/ui/toast'
import type { Label, LabelInput } from '../api'
import { useCardsStore } from '../cardsStore'
import { labelEmojis } from '../cardOptions'
import { errorText } from '../store'

const cards = useCardsStore()
const id = useId()

const blank = (): LabelInput => ({ name: '', color: colorPalette[0], emoji: null })

/** Id of the label being edited, 'new' for the add form, or null. */
const editing = ref<string | null>(null)
const draft = ref<LabelInput>(blank())
const busy = ref(false)
const confirmDelete = ref<string | null>(null)

function startNew() {
  editing.value = 'new'
  draft.value = blank()
}

function startEdit(label: Label) {
  editing.value = label.id
  draft.value = { name: label.name, color: label.color, emoji: label.emoji }
}

async function submit() {
  const input = { ...draft.value, name: draft.value.name.trim() }
  if (!input.name) return
  busy.value = true
  try {
    if (editing.value === 'new') await cards.createLabel(input)
    else await cards.updateLabel(editing.value!, input)
    editing.value = null
  } catch (e) {
    toast(errorText(e, "Couldn't save the label."), 'error')
  } finally {
    busy.value = false
  }
}

async function remove(label: Label) {
  if (confirmDelete.value !== label.id) {
    confirmDelete.value = label.id
    return
  }
  confirmDelete.value = null
  try {
    await cards.removeLabel(label.id)
    toast(`Deleted "${label.name}".`)
  } catch (e) {
    toast(errorText(e, "Couldn't delete the label."), 'error')
  }
}
</script>

<template>
  <section
    class="flex flex-col gap-4 rounded-[20px] border border-line-soft p-5"
    :aria-labelledby="`${id}-title`"
  >
    <h3 :id="`${id}-title`" class="text-base font-semibold">
      Labels <span class="text-muted">{{ cards.labels?.length ?? '' }}</span>
    </h3>
    <p v-if="cards.labels === null" class="text-sm text-muted">Loading…</p>
    <ul v-else-if="cards.labels.length" class="flex flex-col gap-1" aria-label="Board labels">
      <li v-for="label in cards.labels" :key="label.id" class="flex items-center gap-2 text-sm">
        <span
          class="h-3 w-3 shrink-0 rounded-full"
          :style="{ backgroundColor: label.color }"
          aria-hidden="true"
        />
        <span aria-hidden="true">{{ label.emoji }}</span>
        <span class="min-w-0 flex-1 truncate">{{ label.name }}</span>
        <button
          type="button"
          class="ghost rounded-full px-3 py-1 text-xs font-semibold"
          :aria-label="`Edit label ${label.name}`"
          @click="startEdit(label)"
        >
          Edit
        </button>
        <button
          type="button"
          class="ghost rounded-full px-3 py-1 text-xs font-semibold text-red-700"
          :aria-label="
            confirmDelete === label.id
              ? `Confirm delete ${label.name}`
              : `Delete label ${label.name}`
          "
          @click="remove(label)"
        >
          {{ confirmDelete === label.id ? 'Click again' : 'Delete' }}
        </button>
      </li>
    </ul>
    <p v-else class="text-sm text-muted">No labels yet.</p>

    <form
      v-if="editing"
      class="pop flex flex-col gap-3 rounded-xl bg-hover p-3"
      @submit.prevent="submit"
    >
      <label class="flex flex-col gap-1 text-[13px] font-semibold">
        Label name
        <input
          v-model="draft.name"
          type="text"
          maxlength="30"
          required
          class="h-10 rounded-[var(--radius-control)] border border-line bg-panel px-3 text-[15px] font-normal"
        />
      </label>
      <fieldset class="flex flex-col gap-1">
        <legend class="mb-1 text-[13px] font-semibold">Colour</legend>
        <div class="flex flex-wrap gap-1.5">
          <label
            v-for="color in colorPalette"
            :key="color"
            class="h-7 w-7 cursor-pointer rounded-full has-checked:ring-2 has-checked:ring-accent has-checked:ring-offset-2 has-focus-visible:outline-2 has-focus-visible:outline-accent"
            :style="{ backgroundColor: color }"
          >
            <input
              v-model="draft.color"
              type="radio"
              :name="`${id}-color`"
              :value="color"
              class="sr-only"
              :aria-label="`Colour ${color}`"
            />
          </label>
        </div>
      </fieldset>
      <fieldset class="flex flex-col gap-1">
        <legend class="mb-1 text-[13px] font-semibold">Emoji</legend>
        <div class="flex flex-wrap gap-1">
          <label
            v-for="option in [null, ...labelEmojis]"
            :key="option ?? 'none'"
            class="flex h-8 w-8 cursor-pointer items-center justify-center rounded-lg text-base hover:bg-panel has-checked:bg-panel has-checked:ring-2 has-checked:ring-accent has-focus-visible:outline-2 has-focus-visible:outline-accent"
          >
            <input
              v-model="draft.emoji"
              type="radio"
              :name="`${id}-emoji`"
              :value="option"
              class="sr-only"
              :aria-label="option ? `Emoji ${option}` : 'No emoji'"
            />
            <span aria-hidden="true">{{ option ?? '∅' }}</span>
          </label>
        </div>
      </fieldset>
      <div class="flex justify-end gap-2">
        <button
          type="button"
          class="ghost h-9 rounded-full px-4 text-sm font-semibold"
          @click="editing = null"
        >
          Cancel
        </button>
        <button
          type="submit"
          class="press h-9 rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-60"
          :disabled="busy || !draft.name.trim()"
        >
          {{ editing === 'new' ? 'Add label' : 'Save label' }}
        </button>
      </div>
    </form>
    <button
      v-else
      type="button"
      class="ghost h-10 w-fit rounded-full border border-line px-5 text-sm font-semibold"
      @click="startNew"
    >
      + New label
    </button>
  </section>
</template>
