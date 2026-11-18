<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { useRouter } from 'vue-router'

/** Thin accent bar at the top while a navigation takes longer than 120 ms. */
const router = useRouter()
const busy = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined

function stop() {
  clearTimeout(timer)
  busy.value = false
}

const removeBefore = router.beforeEach(() => {
  clearTimeout(timer)
  timer = setTimeout(() => (busy.value = true), 120)
})
const removeAfter = router.afterEach(stop)
const removeError = router.onError(stop)

onBeforeUnmount(() => {
  stop()
  removeBefore()
  removeAfter()
  removeError()
})
</script>

<template>
  <div v-if="busy" class="route-bar" role="progressbar" aria-label="Loading page" />
</template>
