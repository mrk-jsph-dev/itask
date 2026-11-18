<script setup lang="ts">
import { ref, useTemplateRef } from 'vue'

import { useAuthStore } from '@/features/auth/store'
import { useCardDetailStore } from '../cardDetailStore'
import { useBoardsStore } from '../store'
import { formatSize, typeBadge } from '../upload'

const props = defineProps<{ editable: boolean; isDemo: boolean }>()

const detail = useCardDetailStore()
const boards = useBoardsStore()
const auth = useAuthStore()
const input = useTemplateRef('input')
const dragging = ref(false)

const canUpload = () => props.editable && !props.isDemo
const canDelete = (uploadedById: string) =>
  props.editable && (uploadedById === auth.user?.id || boards.isOwner)

function onPick(event: Event) {
  const target = event.target as HTMLInputElement
  void detail.upload([...(target.files ?? [])])
  target.value = ''
}

function onDrop(event: DragEvent) {
  dragging.value = false
  if (!canUpload()) return
  void detail.upload([...(event.dataTransfer?.files ?? [])])
}

function onDragOver(event: DragEvent) {
  if (!canUpload() || !event.dataTransfer?.types.includes('Files')) return
  event.preventDefault()
  dragging.value = true
}
</script>

<template>
  <section
    class="flex flex-col gap-3 rounded-2xl transition-colors"
    :class="{ 'bg-[#EFF4FF] outline-2 outline-dashed outline-accent': dragging }"
    aria-labelledby="attachments-title"
    @dragover="onDragOver"
    @dragleave.self="dragging = false"
    @drop.prevent="onDrop"
  >
    <h3 id="attachments-title" class="text-[13px] font-semibold">📎 Attachments</h3>
    <ul
      v-if="detail.attachments.length || detail.uploading.length"
      class="grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-2"
      aria-label="Attachments"
    >
      <li
        v-for="(file, i) in detail.attachments"
        :key="file.id"
        class="fade-up group relative flex flex-col gap-1.5 rounded-xl border border-line-soft p-3"
        :style="{ '--i': i }"
      >
        <span
          class="w-fit rounded-md bg-hover px-1.5 py-0.5 text-[11px] font-bold tracking-wide text-muted"
          aria-hidden="true"
          >{{ typeBadge(file) }}</span
        >
        <button
          type="button"
          class="truncate text-left text-sm font-medium after:absolute after:inset-0"
          :title="file.fileName"
          :aria-label="`Download ${file.fileName}`"
          @click="detail.download(file.id)"
        >
          {{ file.fileName }}
        </button>
        <span class="text-xs text-muted">{{ formatSize(file.sizeBytes) }}</span>
        <button
          v-if="canDelete(file.uploadedById)"
          type="button"
          class="ghost absolute top-2 right-2 z-10 h-7 w-7 rounded-lg text-xs text-red-700 opacity-60 group-hover:opacity-100"
          :aria-label="`Delete ${file.fileName}`"
          @click="detail.removeAttachment(file.id)"
        >
          ✕
        </button>
      </li>
      <li
        v-for="name in detail.uploading"
        :key="`up-${name}`"
        class="skel flex flex-col gap-1.5 rounded-xl p-3 text-sm text-muted"
        aria-busy="true"
      >
        Uploading {{ name }}…
      </li>
    </ul>
    <p v-else-if="!canUpload()" class="text-sm text-muted">No files.</p>
    <template v-if="canUpload()">
      <input
        ref="input"
        type="file"
        multiple
        class="sr-only"
        aria-label="Upload files"
        @change="onPick"
      />
      <button
        type="button"
        class="ghost flex h-12 items-center justify-center gap-2 rounded-xl border border-dashed border-line text-sm font-semibold text-muted"
        @click="input?.click()"
      >
        + Upload file <span class="font-normal">or drop files here (10 MB max)</span>
      </button>
    </template>
    <p v-else-if="editable && isDemo" class="text-xs text-muted">
      Uploads are off for demo accounts.
    </p>
  </section>
</template>
