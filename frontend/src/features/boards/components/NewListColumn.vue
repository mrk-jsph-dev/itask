<script setup lang="ts">
import { ref } from 'vue'

import type { ListInput } from '../api'
import ListForm from './ListForm.vue'

const emit = defineEmits<{ create: [input: ListInput, done: (ok: boolean) => void] }>()

const open = ref(false)
const busy = ref(false)
const formKey = ref(0)

function submit(input: ListInput) {
  busy.value = true
  emit('create', input, (ok) => {
    busy.value = false
    if (ok) {
      open.value = false
      formKey.value++
    }
  })
}
</script>

<template>
  <div class="min-w-0">
    <ListForm
      v-if="open"
      :key="formKey"
      submit-label="Add list"
      :busy="busy"
      @submit="submit"
      @cancel="open = false"
    />
    <button
      v-else
      type="button"
      class="ghost flex h-24 w-full items-center justify-center rounded-[18px] border-[1.5px] border-dashed border-line text-sm font-semibold text-muted"
      @click="open = true"
    >
      + Add list
    </button>
  </div>
</template>
