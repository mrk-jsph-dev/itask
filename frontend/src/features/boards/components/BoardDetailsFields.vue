<script setup lang="ts">
import { boardColors, boardEmojis } from '../options'

const name = defineModel<string>('name', { required: true })
const emoji = defineModel<string | null>('emoji', { required: true })
const description = defineModel<string | null>('description', { required: true })
const color = defineModel<string>('color', { required: true })

const inputClass =
  'h-[46px] rounded-[var(--radius-control)] border border-line bg-panel px-3.5 text-[15px] font-normal'
</script>

<template>
  <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
    Board name
    <input v-model="name" type="text" maxlength="120" required :class="inputClass" />
  </label>
  <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
    Description
    <textarea
      v-model="description"
      maxlength="500"
      rows="3"
      class="rounded-[var(--radius-control)] border border-line bg-panel px-3.5 py-2.5 text-[15px] font-normal"
    />
  </label>
  <fieldset class="flex flex-col gap-2">
    <legend class="mb-1.5 text-[13px] font-semibold">Emoji</legend>
    <div class="flex flex-wrap gap-1.5">
      <label
        v-for="option in boardEmojis"
        :key="option"
        class="relative flex h-10 w-10 cursor-pointer items-center justify-center rounded-xl text-xl hover:bg-hover has-checked:bg-hover has-checked:ring-2 has-checked:ring-accent has-focus-visible:outline-2 has-focus-visible:outline-accent"
      >
        <input
          v-model="emoji"
          type="radio"
          name="board-emoji"
          :value="option"
          class="sr-only"
          :aria-label="`Emoji ${option}`"
        />
        <span aria-hidden="true">{{ option }}</span>
      </label>
    </div>
  </fieldset>
  <fieldset class="flex flex-col gap-2">
    <legend class="mb-1.5 text-[13px] font-semibold">Colour</legend>
    <div class="flex flex-wrap gap-2">
      <label
        v-for="option in boardColors"
        :key="option"
        class="press relative flex h-9 w-9 cursor-pointer items-center justify-center rounded-full has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-accent"
        :style="{ backgroundColor: option }"
      >
        <input
          v-model="color"
          type="radio"
          name="board-color"
          :value="option"
          class="peer sr-only"
          :aria-label="`Colour ${option}`"
        />
        <span class="hidden text-sm font-bold text-white peer-checked:block" aria-hidden="true"
          >✓</span
        >
      </label>
    </div>
  </fieldset>
</template>
