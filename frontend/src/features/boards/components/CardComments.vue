<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'

import { useAuthStore } from '@/features/auth/store'
import { renderMarkdown } from '@/shared/ui/markdown'
import { toast } from '@/shared/ui/toast'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import type { Comment } from '../api'
import { useCardDetailStore } from '../cardDetailStore'
import { formatRelative } from '../cardOptions'
import { activeMention, highlightMentions, insertMention, suggest } from '../mentions'
import { errorText, useBoardsStore } from '../store'

/** Viewers comment too; `canPost` is false only on archived boards. */
const props = defineProps<{ canPost: boolean }>()

const detail = useCardDetailStore()
const boards = useBoardsStore()
const auth = useAuthStore()

const draft = ref('')
const sending = ref(false)
const composer = useTemplateRef('composer')
const caret = ref(0)
const highlighted = ref(0)
const editingId = ref<string | null>(null)
const editBody = ref('')
const loadingOlder = ref(false)

const mention = computed(() => activeMention(draft.value, caret.value))
const suggestions = computed(() =>
  mention.value ? suggest(boards.members, mention.value.query) : [],
)

const html = (comment: Comment) =>
  highlightMentions(
    renderMarkdown(comment.body),
    comment.mentions.map((m) => m.displayName),
  )

const canEdit = (c: Comment) => props.canPost && c.authorId === auth.user?.id
const canDelete = (c: Comment) => props.canPost && (c.authorId === auth.user?.id || boards.isOwner)

function trackCaret() {
  caret.value = composer.value?.selectionStart ?? draft.value.length
  highlighted.value = 0
}

async function pick(name: string) {
  if (!mention.value) return
  const next = insertMention(draft.value, mention.value.start, caret.value, name)
  draft.value = next.text
  caret.value = next.caret
  const el = composer.value
  if (!el) return
  el.focus()
  await Promise.resolve()
  el.setSelectionRange(next.caret, next.caret)
}

function onKeydown(event: KeyboardEvent) {
  if (suggestions.value.length) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      const step = event.key === 'ArrowDown' ? 1 : -1
      highlighted.value =
        (highlighted.value + step + suggestions.value.length) % suggestions.value.length
      return
    }
    if (event.key === 'Enter' || event.key === 'Tab') {
      event.preventDefault()
      void pick(suggestions.value[highlighted.value]!.displayName)
      return
    }
    if (event.key === 'Escape') {
      event.stopPropagation()
      caret.value = -1
      return
    }
  }
  if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
    event.preventDefault()
    void send()
  }
}

async function send() {
  const body = draft.value.trim()
  if (!body || sending.value) return
  sending.value = true
  try {
    await detail.addComment(body)
    draft.value = ''
  } catch (e) {
    toast(errorText(e, "Couldn't post the comment."), 'error')
  } finally {
    sending.value = false
  }
}

function startEdit(comment: Comment) {
  editingId.value = comment.id
  editBody.value = comment.body
}

async function saveEdit(comment: Comment) {
  const body = editBody.value.trim()
  if (!body) return
  try {
    if (body !== comment.body) await detail.editComment(comment.id, body)
    editingId.value = null
  } catch (e) {
    toast(errorText(e, "Couldn't save the comment."), 'error')
  }
}

async function older() {
  loadingOlder.value = true
  try {
    await detail.loadOlder()
  } catch (e) {
    toast(errorText(e, "Couldn't load older comments."), 'error')
  } finally {
    loadingOlder.value = false
  }
}
</script>

<template>
  <section class="flex flex-col gap-3" aria-labelledby="comments-title">
    <h3 id="comments-title" class="text-[13px] font-semibold">💬 Comments</h3>
    <button
      v-if="detail.nextCursor"
      type="button"
      class="ghost w-fit rounded-full px-3 py-1 text-sm font-semibold text-muted"
      :disabled="loadingOlder"
      @click="older"
    >
      {{ loadingOlder ? 'Loading…' : 'Show older comments' }}
    </button>
    <ol class="flex flex-col gap-4" aria-label="Comments">
      <li v-for="comment in detail.comments" :key="comment.id" class="fade-up flex gap-3">
        <UserAvatar :name="comment.authorName" :color="comment.authorColor" :size="30" />
        <div class="flex min-w-0 flex-1 flex-col gap-1">
          <p class="flex flex-wrap items-baseline gap-x-2 text-sm">
            <span class="font-semibold">{{ comment.authorName }}</span>
            <time class="text-xs text-muted" :datetime="comment.createdAt">
              {{ formatRelative(comment.createdAt) }}
            </time>
            <span v-if="comment.editedAt" class="text-xs text-muted">(edited)</span>
          </p>
          <form
            v-if="editingId === comment.id"
            class="flex flex-col gap-2"
            @submit.prevent="saveEdit(comment)"
          >
            <textarea
              v-model="editBody"
              rows="3"
              maxlength="5000"
              aria-label="Edit comment"
              class="rounded-[var(--radius-control)] border border-line px-3 py-2 text-[15px]"
              @keydown.esc.stop="editingId = null"
            />
            <div class="flex gap-2">
              <button
                type="submit"
                class="press h-8 rounded-full bg-accent px-3 text-sm font-semibold text-white"
              >
                Save
              </button>
              <button
                type="button"
                class="ghost h-8 rounded-full px-3 text-sm"
                @click="editingId = null"
              >
                Cancel
              </button>
            </div>
          </form>
          <!-- eslint-disable-next-line vue/no-v-html -- sanitised by renderMarkdown -->
          <div v-else class="md-body text-[15px]" v-html="html(comment)" />
          <div v-if="editingId !== comment.id" class="flex gap-3 text-xs">
            <button
              v-if="canEdit(comment)"
              type="button"
              class="font-semibold text-muted hover:text-ink"
              @click="startEdit(comment)"
            >
              Edit
            </button>
            <button
              v-if="canDelete(comment)"
              type="button"
              class="font-semibold text-muted hover:text-red-700"
              @click="detail.removeComment(comment.id)"
            >
              Delete
            </button>
          </div>
        </div>
      </li>
    </ol>
    <p v-if="!detail.comments.length && !detail.loading" class="text-sm text-muted">
      No comments yet.
    </p>

    <form v-if="canPost" class="relative flex gap-3" @submit.prevent="send">
      <UserAvatar
        :name="auth.user?.displayName ?? ''"
        :email="auth.user?.email"
        :color="auth.user?.avatarColor"
        :size="30"
      />
      <div class="flex min-w-0 flex-1 flex-col gap-2">
        <textarea
          ref="composer"
          v-model="draft"
          rows="2"
          maxlength="5000"
          placeholder="Write a comment. Use @ to mention someone."
          aria-label="Write a comment"
          aria-autocomplete="list"
          :aria-expanded="suggestions.length > 0"
          aria-controls="mention-list"
          class="rounded-[var(--radius-control)] border border-line bg-panel px-3 py-2 text-[15px]"
          @input="trackCaret"
          @click="trackCaret"
          @keyup="(e) => !['ArrowUp', 'ArrowDown'].includes(e.key) && trackCaret()"
          @keydown="onKeydown"
        />
        <ul
          v-if="suggestions.length"
          id="mention-list"
          role="listbox"
          aria-label="Mention a member"
          class="pop absolute bottom-full left-10 z-20 mb-1 flex w-56 flex-col rounded-[var(--radius-control)] border border-line-soft bg-panel p-1 text-sm shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
        >
          <li
            v-for="(member, i) in suggestions"
            :key="member.userId"
            role="option"
            :aria-selected="i === highlighted"
            class="cursor-pointer rounded-lg px-3 py-2"
            :class="{ 'bg-hover': i === highlighted }"
            @mousedown.prevent="pick(member.displayName)"
          >
            @{{ member.displayName }}
          </li>
        </ul>
        <button
          type="submit"
          class="press h-9 w-fit rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-50"
          :disabled="!draft.trim() || sending"
        >
          Send
        </button>
      </div>
    </form>
  </section>
</template>
