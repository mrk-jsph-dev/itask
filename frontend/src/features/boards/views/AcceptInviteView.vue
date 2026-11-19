<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import { boardsApi } from '../api'

const props = defineProps<{ token: string }>()
const router = useRouter()

const error = ref<string | null>(null)

function describe(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 404) return 'This invite link is not valid or was revoked.'
    if (e.status === 410) return 'This invite has expired or was already used.'
    if (e.status === 403) return e.message
  }
  return "Couldn't accept the invite. Try again later."
}

onMounted(async () => {
  try {
    const { boardId } = await boardsApi.acceptInvite(props.token)
    toast('You joined the board.')
    await router.replace({ name: 'board', params: { boardId } })
  } catch (e) {
    error.value = describe(e)
  }
})
</script>

<template>
  <section class="mx-auto flex max-w-md flex-col items-center gap-3 px-5 py-10 text-center">
    <template v-if="error">
      <span class="text-4xl" aria-hidden="true">✉️</span>
      <h1 class="text-xl font-bold tracking-tight">Invite not accepted</h1>
      <p role="alert" class="text-sm text-muted">{{ error }}</p>
      <RouterLink to="/" class="text-sm font-semibold">Go to Home</RouterLink>
    </template>
    <template v-else>
      <span class="skel h-10 w-10 rounded-full" aria-hidden="true" />
      <p class="text-sm text-muted" role="status">Joining the board…</p>
    </template>
  </section>
</template>
