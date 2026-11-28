<script setup lang="ts">
import { ref, useTemplateRef } from 'vue'

import { useAuthStore } from '@/features/auth/store'
import { useCardDetailStore } from '../cardDetailStore'
import { useBoardsStore } from '../store'
import { badgeClass, formatSize, typeBadge } from '../upload'

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
    :class="{ 'bg-accent-soft outline-2 outline-dashed outline-accent': dragging }"
    aria-labelledby="attachments-title"
    @dragover="onDragOver"
    @dragleave.self="dragging = false"
    @drop.prevent="onDrop"
  >
    <h3 id="attachments-title" class="text-base font-semibold">📎 Attachments</h3>
    <ul
      class="grid grid-cols-[repeat(auto-fill,minmax(200px,1fr))] gap-2.5"
      aria-label="Attachments"
    >
      <li
        v-for="(file, i) in detail.attachments"
        :key="file.id"
        class="fade-up group relative flex items-center gap-2.5 rounded-xl border border-line-soft p-3"
        :style="{ '--i': i }"
      >
        <span
          class="flex h-10 w-10 shrink-0 items-center justify-center rounded-[10px] text-[11px] font-bold"
          :class="badgeClass(file)"
          aria-hidden="true"
          >{{ typeBadge(file) }}</span
        >
        <span class="flex min-w-0 flex-col pr-6">
          <button
            type="button"
            class="truncate text-left text-[13px] font-semibold after:absolute after:inset-0"
            :title="file.fileName"
            :aria-label="`Download ${file.fileName}`"
            @click="detail.download(file.id)"
          >
            {{ file.fileName }}
          </button>
          <span class="text-xs text-muted">{{ formatSize(file.sizeBytes) }}</span>
        </span>
        <button
          v-if="canDelete(file.uploadedById)"
          type="button"
          class="ghost absolute top-2 right-2 z-10 h-7 w-7 rounded-lg text-xs text-danger opacity-60 group-hover:opacity-100"
          :aria-label="`Delete ${file.fileName}`"
          @click="detail.removeAttachment(file.id)"
        >
          ✕
        </button>
      </li>
      <li
        v-for="name in detail.uploading"
        :key="`up-${name}`"
        class="skel flex min-h-[66px] items-center rounded-xl p-3 text-sm text-muted"
        aria-busy="true"
      >
        Uploading {{ name }}…
      </li>
      <li v-if="canUpload()" class="flex">
        <button
          type="button"
          class="ghost flex min-h-[66px] flex-1 flex-col items-center justify-center rounded-xl border-[1.5px] border-dashed border-line bg-panel text-[13px] font-medium text-ink-soft"
          title="Or drop files here (10 MB max)"
          @click="input?.click()"
        >
          + Upload file
          <span class="text-xs font-normal text-muted">or drop here, 10 MB max</span>
        </button>
      </li>
    </ul>
    <p v-if="!detail.attachments.length && !canUpload()" class="text-sm text-muted">No files.</p>
    <input
      v-if="canUpload()"
      ref="input"
      type="file"
      multiple
      class="sr-only"
      aria-label="Upload files"
      @change="onPick"
    />
    <p v-if="editable && isDemo" class="text-xs text-muted">Uploads are off for demo accounts.</p>
  </section>
</template>
