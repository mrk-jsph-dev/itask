<script setup lang="ts">
import { ref, useId } from 'vue'

import type { ListInput } from '../api'
import { listEmojis } from '../options'

const props = withDefaults(
  defineProps<{ initial?: ListInput; submitLabel: string; busy?: boolean }>(),
  { initial: () => ({ name: '', emoji: '📝', wipLimit: null }), busy: false },
)
const emit = defineEmits<{ submit: [input: ListInput]; cancel: [] }>()

const id = useId()
const name = ref(props.initial.name)
const emoji = ref<string | null>(props.initial.emoji)
const wipLimit = ref<number | ''>(props.initial.wipLimit ?? '')

function submit() {
  const trimmed = name.value.trim()
  if (!trimmed) return
  emit('submit', {
    name: trimmed,
    emoji: emoji.value,
    wipLimit: wipLimit.value === '' ? null : Number(wipLimit.value),
  })
}
</script>

<template>
  <form
    class="fade-in flex flex-col gap-3 rounded-[18px] border border-line bg-panel p-3"
    @submit.prevent="submit"
    @keydown.esc.stop="emit('cancel')"
  >
    <label class="flex flex-col gap-1 text-[13px] font-semibold">
      List name
      <input
        v-model="name"
        type="text"
        maxlength="60"
        required
        autofocus
        class="h-10 rounded-[var(--radius-control)] border border-line bg-panel px-3 text-[15px] font-normal"
      />
    </label>
    <fieldset class="flex flex-col gap-1">
      <legend class="mb-1 text-[13px] font-semibold">Emoji</legend>
      <div class="flex flex-wrap gap-1">
        <label
          v-for="option in listEmojis"
          :key="option"
          class="flex h-8 w-8 cursor-pointer items-center justify-center rounded-lg text-base hover:bg-hover has-checked:bg-hover has-checked:ring-2 has-checked:ring-accent has-focus-visible:outline-2 has-focus-visible:outline-accent"
        >
          <input
            v-model="emoji"
            type="radio"
            :name="`list-emoji-${id}`"
            :value="option"
            class="sr-only"
            :aria-label="`Emoji ${option}`"
          />
          <span aria-hidden="true">{{ option }}</span>
        </label>
      </div>
    </fieldset>
    <label class="flex items-center justify-between gap-3 text-[13px] font-semibold">
      WIP limit
      <input
        v-model="wipLimit"
        type="number"
        min="1"
        max="999"
        placeholder="None"
        class="h-9 w-24 rounded-[var(--radius-control)] border border-line bg-panel px-3 text-[15px] font-normal"
      />
    </label>
    <div class="flex justify-end gap-2">
      <button
        type="button"
        class="ghost h-9 rounded-full px-4 text-sm font-semibold"
        @click="emit('cancel')"
      >
        Cancel
      </button>
      <button
        type="submit"
        class="press h-9 rounded-full bg-accent px-4 text-sm font-semibold text-on-accent disabled:opacity-60"
        :disabled="busy || !name.trim()"
      >
        {{ submitLabel }}
      </button>
    </div>
  </form>
</template>
