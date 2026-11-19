<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import { startExternal, type ExternalProvider } from '@/features/auth/external'
import { profileApi, type MyLogins } from '../api'
import { canDisconnect, connectionRows } from '../connections'

const props = defineProps<{ hasPassword: boolean }>()

const logins = ref<MyLogins | null>(null)
const busy = ref<ExternalProvider | null>(null)
const confirming = ref<ExternalProvider | null>(null)

const rows = computed(() => (logins.value ? connectionRows(logins.value) : []))
const removable = computed(() => canDisconnect(props.hasPassword, logins.value?.logins.length ?? 0))

onMounted(async () => {
  try {
    logins.value = await profileApi.logins()
  } catch {
    logins.value = { hasPassword: props.hasPassword, logins: [], providers: [] }
  }
})

function problem(e: unknown) {
  return e instanceof ApiError && e.status < 500 ? e.message : 'Something went wrong.'
}

async function connect(provider: ExternalProvider) {
  busy.value = provider
  try {
    const { ticket } = await profileApi.startLink(provider)
    startExternal(provider, { link: ticket })
  } catch (e) {
    busy.value = null
    toast(problem(e), 'error')
  }
}

async function disconnect(provider: ExternalProvider) {
  if (confirming.value !== provider) {
    confirming.value = provider
    return
  }
  confirming.value = null
  busy.value = provider
  try {
    await profileApi.removeLogin(provider)
    if (logins.value) {
      logins.value = {
        ...logins.value,
        logins: logins.value.logins.filter((l) => l.provider !== provider),
      }
    }
    toast('Disconnected.')
  } catch (e) {
    toast(problem(e), 'error')
  } finally {
    busy.value = null
  }
}
</script>

<template>
  <section
    v-if="!logins || rows.length"
    class="flex flex-col gap-4 rounded-[20px] border border-line-soft p-5"
    aria-labelledby="profile-connections"
  >
    <div class="flex flex-col gap-1">
      <h2 id="profile-connections" class="text-base font-semibold">Connected accounts</h2>
      <p class="text-sm text-muted">Sign in with these instead of a password.</p>
    </div>
    <div v-if="!logins" class="flex flex-col gap-3">
      <SkeletonBlock height="44px" />
      <SkeletonBlock height="44px" />
    </div>
    <ul v-else class="flex flex-col gap-2">
      <li
        v-for="(row, i) in rows"
        :key="row.provider"
        class="fade-up flex flex-wrap items-center justify-between gap-3 rounded-2xl bg-hover px-4 py-3"
        :style="{ '--i': i }"
      >
        <div class="flex min-w-0 flex-col">
          <span class="text-sm font-semibold">{{ row.label }}</span>
          <span class="truncate text-xs text-muted">
            {{ row.connected ? (row.accountName ?? 'Connected') : 'Not connected' }}
          </span>
        </div>
        <button
          v-if="!row.connected"
          type="button"
          :disabled="busy !== null"
          class="press h-9 rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-60"
          :aria-label="`Connect ${row.label}`"
          @click="connect(row.provider)"
        >
          {{ busy === row.provider ? 'Opening…' : 'Connect' }}
        </button>
        <button
          v-else
          type="button"
          :disabled="busy !== null || !removable"
          :title="removable ? undefined : 'Set a password first, so you can still sign in.'"
          class="ghost h-9 rounded-full border border-line px-4 text-sm font-semibold disabled:opacity-60"
          :class="confirming === row.provider ? 'border-red-300 text-red-600' : ''"
          :aria-label="`Disconnect ${row.label}`"
          @click="disconnect(row.provider)"
          @blur="confirming = null"
        >
          {{ confirming === row.provider ? 'Click again to disconnect' : 'Disconnect' }}
        </button>
      </li>
    </ul>
    <p v-if="logins && !removable && logins.logins.length" class="text-xs text-muted">
      Set a password below before disconnecting your only sign-in method.
    </p>
  </section>
</template>
