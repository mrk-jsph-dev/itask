<script setup lang="ts">
import { onMounted, ref } from 'vue'

import { authApi } from '../api'
import { providerLabels, startExternal, type ExternalProvider } from '../external'

const props = defineProps<{ next?: string; disabled?: boolean }>()

const providers = ref<ExternalProvider[]>([])
const leaving = ref<ExternalProvider | null>(null)

onMounted(async () => {
  try {
    providers.value = (await authApi.providers()).providers
  } catch {
    providers.value = []
  }
})

function go(provider: ExternalProvider) {
  leaving.value = provider
  startExternal(provider, { next: props.next })
}
</script>

<template>
  <section
    v-if="providers.length"
    class="fade-in flex flex-col gap-3"
    aria-labelledby="external-title"
  >
    <div class="flex items-center gap-3 text-xs font-semibold text-muted uppercase">
      <span class="h-px flex-1 bg-line-soft" />
      <h2 id="external-title">Or continue with</h2>
      <span class="h-px flex-1 bg-line-soft" />
    </div>
    <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
      <button
        v-for="(provider, i) in providers"
        :key="provider"
        type="button"
        :disabled="disabled || leaving !== null"
        :style="{ '--i': i }"
        class="fade-up ghost flex h-11 items-center justify-center gap-2 rounded-full border border-line text-sm font-semibold disabled:opacity-60"
        :aria-label="`Continue with ${providerLabels[provider]}`"
        @click="go(provider)"
      >
        <span v-if="leaving === provider" class="spinner" aria-hidden="true" />
        <svg
          v-else-if="provider === 'github'"
          viewBox="0 0 24 24"
          width="18"
          height="18"
          fill="currentColor"
          aria-hidden="true"
        >
          <path
            d="M12 .5a11.5 11.5 0 0 0-3.64 22.41c.58.1.79-.25.79-.56v-2c-3.2.7-3.88-1.37-3.88-1.37-.52-1.33-1.28-1.69-1.28-1.69-1.05-.71.08-.7.08-.7 1.16.08 1.77 1.19 1.77 1.19 1.03 1.77 2.7 1.26 3.36.96.1-.75.4-1.26.73-1.55-2.55-.29-5.24-1.28-5.24-5.68 0-1.25.45-2.28 1.19-3.08-.12-.29-.52-1.46.11-3.04 0 0 .97-.31 3.17 1.18a11 11 0 0 1 5.77 0c2.2-1.49 3.17-1.18 3.17-1.18.63 1.58.23 2.75.11 3.04.74.8 1.19 1.83 1.19 3.08 0 4.41-2.69 5.38-5.25 5.67.41.36.78 1.06.78 2.14v3.17c0 .31.21.67.8.56A11.5 11.5 0 0 0 12 .5Z"
          />
        </svg>
        <svg v-else viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
          <rect x="1" y="1" width="10" height="10" fill="#F25022" />
          <rect x="13" y="1" width="10" height="10" fill="#7FBA00" />
          <rect x="1" y="13" width="10" height="10" fill="#00A4EF" />
          <rect x="13" y="13" width="10" height="10" fill="#FFB900" />
        </svg>
        {{ providerLabels[provider] }}
      </button>
    </div>
  </section>
</template>
