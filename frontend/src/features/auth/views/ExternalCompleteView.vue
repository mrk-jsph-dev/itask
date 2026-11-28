<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import AppLogo from '@/shared/ui/AppLogo.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import { externalErrorMessage, safeNext, takePendingExternal } from '../external'
import { useAuthStore } from '../store'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const error = ref<string | null>(null)

onMounted(async () => {
  const code = typeof route.query.code === 'string' ? route.query.code : null
  const failure = typeof route.query.error === 'string' ? route.query.error : null
  const { nonce, next } = takePendingExternal()
  // The code is single use; keep it out of history either way.
  window.history.replaceState(window.history.state, '', route.path)

  if (failure || !code) {
    error.value = externalErrorMessage(failure)
  } else if (!nonce) {
    error.value = 'This sign-in was started in another tab or has expired. Please try again.'
  } else {
    try {
      const mode = await auth.completeExternal(code, nonce)
      if (mode === 'link') {
        toast('Account connected.')
        await router.replace('/profile')
      } else {
        await router.replace(safeNext(next) ?? '/')
      }
      return
    } catch (e) {
      error.value =
        e instanceof ApiError && e.status < 500
          ? e.message
          : 'Sign-in did not finish. Please try again.'
    }
  }

  await auth.ensureLoaded().catch(() => undefined)
})
</script>

<template>
  <div class="flex min-h-screen items-center justify-center p-4">
    <div
      class="fade-up flex w-full max-w-[420px] flex-col gap-6 rounded-[28px] bg-panel p-8 shadow-card"
    >
      <div class="flex items-center gap-2.5">
        <AppLogo />
        <span class="text-xl font-bold">iTask</span>
      </div>
      <template v-if="error">
        <h1 class="text-2xl font-bold tracking-[-0.02em]">Couldn't sign you in</h1>
        <p role="alert" class="text-[15px] text-muted">{{ error }}</p>
        <RouterLink
          :to="auth.isSignedIn ? '/profile' : '/sign-in'"
          class="press flex h-12 items-center justify-center rounded-full bg-accent text-[15px] font-semibold text-on-accent no-underline"
          >{{ auth.isSignedIn ? 'Back to your profile' : 'Back to sign in' }}</RouterLink
        >
      </template>
      <template v-else>
        <h1 class="text-2xl font-bold tracking-[-0.02em]">Signing you in…</h1>
        <div class="flex flex-col gap-2" aria-hidden="true">
          <SkeletonBlock height="14px" />
          <SkeletonBlock height="14px" width="70%" />
        </div>
      </template>
    </div>
  </div>
</template>
