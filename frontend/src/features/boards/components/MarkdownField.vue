<script setup lang="ts">
import { computed, ref } from 'vue'

import { renderMarkdown } from '@/shared/ui/markdown'

defineProps<{
  label: string
  editable: boolean
  maxlength: number
  placeholder?: string
  /** Shown instead of the empty-state text while the value loads. */
  loading?: boolean
}>()
const model = defineModel<string>({ required: true })
const emit = defineEmits<{ save: [] }>()

const tab = ref<'write' | 'preview'>('write')
const html = computed(() => renderMarkdown(model.value))

const tabClass = (active: boolean) => [
  'rounded-full px-3 py-1 text-[13px] font-semibold',
  active ? 'bg-ink text-panel' : 'ghost text-muted',
]
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex items-center justify-between gap-2">
      <span class="text-base font-semibold">{{ label }}</span>
      <div v-if="editable" class="flex gap-1" role="tablist" :aria-label="`${label} mode`">
        <button
          type="button"
          role="tab"
          :aria-selected="tab === 'write'"
          :class="tabClass(tab === 'write')"
          @click="tab = 'write'"
        >
          Write
        </button>
        <button
          type="button"
          role="tab"
          :aria-selected="tab === 'preview'"
          :class="tabClass(tab === 'preview')"
          @click="tab = 'preview'"
        >
          Preview
        </button>
      </div>
    </div>
    <textarea
      v-if="editable && tab === 'write'"
      v-model="model"
      rows="6"
      :maxlength="maxlength"
      :aria-label="label"
      :placeholder="loading ? 'Loading…' : placeholder"
      class="rounded-[var(--radius-control)] border border-line bg-panel px-3 py-2 text-[15px]"
      @keydown.meta.enter.prevent="emit('save')"
      @keydown.ctrl.enter.prevent="emit('save')"
    />
    <!-- eslint-disable-next-line vue/no-v-html -- sanitised by renderMarkdown -->
    <div v-else-if="model.trim()" class="md-body text-[15px]" v-html="html" />
    <p v-else class="text-sm text-muted">{{ loading ? 'Loading…' : 'No description.' }}</p>
    <slot />
  </div>
</template>
