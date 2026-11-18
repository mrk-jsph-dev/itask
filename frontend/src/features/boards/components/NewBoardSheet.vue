<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'

import SideSheet from '@/shared/ui/SideSheet.vue'
import BoardDetailsFields from './BoardDetailsFields.vue'
import { boardColors } from '../options'
import { errorText, useBoardsStore } from '../store'

const emit = defineEmits<{ close: [] }>()

const store = useBoardsStore()
const router = useRouter()

const name = ref('')
const emoji = ref<string | null>('📋')
const description = ref<string | null>('')
const color = ref<string>(boardColors[0])
const saving = ref(false)
const error = ref<string | null>(null)

async function submit() {
  saving.value = true
  error.value = null
  try {
    const created = await store.create({
      name: name.value.trim(),
      emoji: emoji.value,
      description: description.value?.trim() || null,
      color: color.value,
    })
    emit('close')
    await router.push({ name: 'board', params: { boardId: created.id } })
  } catch (e) {
    error.value = errorText(e)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <SideSheet title="New board" @close="emit('close')">
    <form class="flex flex-col gap-4" @submit.prevent="submit">
      <BoardDetailsFields
        v-model:name="name"
        v-model:emoji="emoji"
        v-model:description="description"
        v-model:color="color"
      />
      <p v-if="error" role="alert" class="text-sm text-red-600">{{ error }}</p>
      <div class="flex gap-2">
        <button
          type="button"
          class="ghost h-11 rounded-full border border-line px-5 text-sm font-semibold"
          @click="emit('close')"
        >
          Cancel
        </button>
        <button
          type="submit"
          :disabled="saving || !name.trim()"
          class="press h-11 rounded-full bg-accent px-6 text-sm font-semibold text-white disabled:opacity-60"
        >
          {{ saving ? 'Creating…' : 'Create board' }}
        </button>
      </div>
    </form>
  </SideSheet>
</template>
