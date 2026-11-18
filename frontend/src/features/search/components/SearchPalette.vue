<script setup lang="ts">
import { computed, onBeforeUnmount } from 'vue'
import { useRouter } from 'vue-router'

import CommandPalette from '@/shared/ui/CommandPalette.vue'

import { useSearchStore, type SearchItem } from '../store'

const props = defineProps<{ userId: string }>()
const emit = defineEmits<{ close: [] }>()

const search = useSearchStore()
const router = useRouter()
search.setUser(props.userId)

const query = computed({ get: () => search.query, set: (value: string) => search.setQuery(value) })

const emptyText = computed(() => {
  if (search.failed) return 'Search failed. Try again in a moment.'
  if (search.searching) return '🌱 Nothing matches that yet.'
  return 'Type at least 2 letters, or a card key like ITK-42.'
})

async function open(item: SearchItem) {
  search.remember(item)
  emit('close')
  await router.push(item.to)
}

onBeforeUnmount(() => search.reset())
</script>

<template>
  <CommandPalette
    v-model:query="query"
    label="Search boards, cards and comments"
    placeholder="Search boards, cards, comments or a key"
    :sections="search.sections"
    :loading="search.loading"
    :empty-text="emptyText"
    @select="open"
    @close="emit('close')"
  />
</template>
