<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { useAuthStore } from '@/features/auth/store'
import SideSheet from '@/shared/ui/SideSheet.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { colorPalette } from '@/shared/ui/palette'
import { toast } from '@/shared/ui/toast'
import type { CardPatch, CardPriority } from '../api'
import { useCardDetailStore } from '../cardDetailStore'
import { useCardsStore } from '../cardsStore'
import { fromDateInput, priorities, toDateInput } from '../cardOptions'
import { useListsStore } from '../listsStore'
import { errorText, useBoardsStore } from '../store'
import CardAttachments from './CardAttachments.vue'
import CardChecklist from './CardChecklist.vue'
import CardComments from './CardComments.vue'
import MarkdownField from './MarkdownField.vue'

/** `canComment`: any member may comment unless the board is archived. */
const props = defineProps<{ cardId: string; editable: boolean; canComment: boolean }>()
const emit = defineEmits<{ close: [] }>()

const boards = useBoardsStore()
const lists = useListsStore()
const cards = useCardsStore()
const detail = useCardDetailStore()
const auth = useAuthStore()

/** Live summary from the board, so tile and sheet stay in step. */
const spot = computed(() => cards.find(props.cardId))
const card = computed(() => (spot.value ? spot.value.list.cards[spot.value.index]! : null))

const title = ref(card.value?.title ?? '')
const description = ref('')
const descriptionSaved = ref('')

watch(
  () => props.cardId,
  async (id) => {
    try {
      await detail.open(lists.boardId!, id)
      description.value = detail.description
      descriptionSaved.value = description.value
    } catch (e) {
      toast(errorText(e, "Couldn't load the card."), 'error')
    }
  },
  { immediate: true },
)
onBeforeUnmount(() => detail.close())

watch(
  () => card.value?.title,
  (value) => {
    if (value !== undefined) title.value = value
  },
)

// The card left the board (archived elsewhere or the list reloaded without it).
watch(card, (value) => {
  if (!value) emit('close')
})

async function save(patch: CardPatch) {
  try {
    await cards.update(props.cardId, patch)
    return true
  } catch (e) {
    toast(errorText(e, "Couldn't save the card."), 'error')
    return false
  }
}

async function saveTitle() {
  const trimmed = title.value.trim()
  if (!card.value || trimmed === card.value.title) return
  if (!trimmed) {
    title.value = card.value.title
    return
  }
  await save({ title: trimmed })
}

async function saveDescription() {
  if (await save({ description: description.value.trim() || null })) {
    descriptionSaved.value = description.value
    toast('Description saved.')
  }
}

function onPriority(event: Event) {
  void save({ priority: (event.target as HTMLSelectElement).value as CardPriority })
}

function onDate(field: 'dueAt' | 'startAt', event: Event) {
  const value = (event.target as HTMLInputElement).value
  void save({ [field]: fromDateInput(value, field === 'dueAt' ? 'end' : 'start') })
}

async function toggleLabel(labelId: string) {
  if (!card.value) return
  const ids = card.value.labels.map((l) => l.id)
  const next = ids.includes(labelId) ? ids.filter((id) => id !== labelId) : [...ids, labelId]
  try {
    await cards.setLabels(props.cardId, next)
  } catch (e) {
    toast(errorText(e, "Couldn't change the labels."), 'error')
  }
}

async function toggleAssignee(userId: string) {
  if (!card.value) return
  const ids = card.value.assignees.map((a) => a.userId)
  const next = ids.includes(userId) ? ids.filter((id) => id !== userId) : [...ids, userId]
  try {
    await cards.setAssignees(props.cardId, next)
  } catch (e) {
    toast(errorText(e, "Couldn't change the assignees."), 'error')
  }
}

function archive() {
  emit('close')
  void cards.archive(props.cardId)
}

const fieldClass =
  'h-10 rounded-[var(--radius-control)] border border-line bg-panel px-3 text-[15px] font-normal disabled:bg-hover'
const sectionTitle = 'text-[13px] font-semibold'
</script>

<template>
  <SideSheet :title="card ? card.key : 'Card'" wide @close="emit('close')">
    <template v-if="card && spot">
      <p class="-mt-3 text-sm text-muted">
        {{ boards.current?.name }} / <span aria-hidden="true">{{ spot.list.emoji }}</span>
        {{ spot.list.name }}
      </p>
      <fieldset :disabled="!editable" class="flex flex-col gap-5">
        <legend class="sr-only">Card details</legend>
        <div class="flex items-start gap-3">
          <label class="flex min-w-0 flex-1 flex-col gap-1">
            <span class="sr-only">Title</span>
            <textarea
              v-model="title"
              rows="2"
              maxlength="200"
              class="resize-none rounded-[var(--radius-control)] border border-transparent px-2 py-1 text-xl font-bold tracking-tight hover:border-line focus:border-line disabled:bg-transparent"
              @keydown.enter.prevent="($event.target as HTMLTextAreaElement).blur()"
              @blur="saveTitle"
            />
          </label>
        </div>
        <button
          type="button"
          class="press h-10 w-fit rounded-full px-5 text-sm font-semibold"
          :class="card.completedAt ? 'border border-line bg-panel' : 'bg-emerald-600 text-white'"
          @click="cards.toggleComplete(card)"
        >
          {{ card.completedAt ? '↺ Reopen' : '✓ Mark complete' }}
        </button>

        <div class="grid grid-cols-2 gap-3">
          <label class="col-span-2 flex flex-col gap-1" :class="sectionTitle">
            Priority
            <select :value="card.priority" :class="fieldClass" @change="onPriority">
              <option v-for="p in priorities" :key="p" :value="p">{{ p }}</option>
            </select>
          </label>
          <label class="flex flex-col gap-1" :class="sectionTitle">
            Start date
            <input
              type="date"
              :value="toDateInput(card.startAt)"
              :class="fieldClass"
              @change="onDate('startAt', $event)"
            />
          </label>
          <label class="flex flex-col gap-1" :class="sectionTitle">
            Due date
            <input
              type="date"
              :value="toDateInput(card.dueAt)"
              :class="fieldClass"
              @change="onDate('dueAt', $event)"
            />
          </label>
        </div>

        <fieldset class="flex flex-col gap-2">
          <legend :class="[sectionTitle, 'mb-1']">Cover colour</legend>
          <div class="flex flex-wrap gap-1.5">
            <button
              type="button"
              class="flex h-8 w-8 items-center justify-center rounded-lg border border-line text-xs text-muted"
              :class="{ 'ring-2 ring-accent': !card.coverColor }"
              :aria-pressed="!card.coverColor"
              aria-label="No cover"
              @click="save({ coverColor: null })"
            >
              ✕
            </button>
            <button
              v-for="color in colorPalette"
              :key="color"
              type="button"
              class="h-8 w-8 rounded-lg"
              :class="{ 'ring-2 ring-accent ring-offset-2': card.coverColor === color }"
              :style="{ backgroundColor: color }"
              :aria-pressed="card.coverColor === color"
              :aria-label="`Cover ${color}`"
              @click="save({ coverColor: color })"
            />
          </div>
        </fieldset>

        <fieldset class="flex flex-col gap-2">
          <legend :class="[sectionTitle, 'mb-1']">Labels</legend>
          <p v-if="cards.labels?.length === 0" class="text-sm text-muted">
            No labels yet. Add some in board settings.
          </p>
          <div class="flex flex-wrap gap-1.5">
            <button
              v-for="label in cards.labels ?? []"
              :key="label.id"
              type="button"
              class="flex items-center gap-1.5 rounded-full border px-3 py-1 text-[13px]"
              :class="
                card.labels.some((l) => l.id === label.id)
                  ? 'border-accent bg-[#EFF4FF] font-semibold'
                  : 'border-line-soft'
              "
              :aria-pressed="card.labels.some((l) => l.id === label.id)"
              @click="toggleLabel(label.id)"
            >
              <span v-if="label.emoji" aria-hidden="true">{{ label.emoji }}</span>
              <span
                v-else
                class="h-2 w-2 rounded-full"
                :style="{ backgroundColor: label.color }"
                aria-hidden="true"
              />
              {{ label.name }}
            </button>
          </div>
        </fieldset>

        <fieldset class="flex flex-col gap-2">
          <legend :class="[sectionTitle, 'mb-1']">Assignees</legend>
          <ul class="flex flex-col gap-1">
            <li v-for="member in boards.members" :key="member.userId">
              <label
                class="flex cursor-pointer items-center gap-3 rounded-xl px-2 py-1.5 hover:bg-hover"
              >
                <input
                  type="checkbox"
                  class="h-4 w-4 accent-accent"
                  :checked="card.assignees.some((a) => a.userId === member.userId)"
                  @change="toggleAssignee(member.userId)"
                />
                <UserAvatar
                  :name="member.displayName"
                  :email="member.email"
                  :color="member.avatarColor"
                  :size="26"
                />
                <span class="truncate text-sm">{{ member.displayName || member.email }}</span>
              </label>
            </li>
          </ul>
        </fieldset>
      </fieldset>

      <MarkdownField
        v-model="description"
        label="📝 Description"
        :editable="editable"
        :maxlength="10000"
        :loading="detail.loading"
        placeholder="Add more detail (Markdown)"
        @save="saveDescription"
      >
        <button
          v-if="editable && description !== descriptionSaved"
          type="button"
          class="press h-9 w-fit rounded-full bg-accent px-4 text-sm font-semibold text-white"
          @click="saveDescription"
        >
          Save description
        </button>
      </MarkdownField>

      <div v-if="detail.loading" class="flex flex-col gap-2" aria-busy="true">
        <SkeletonBlock height="18px" width="30%" />
        <SkeletonBlock height="64px" />
      </div>
      <template v-else>
        <CardChecklist :editable="editable" />
        <CardAttachments :editable="editable" :is-demo="!!auth.user?.isDemo" />
        <CardComments :can-post="canComment" />
      </template>

      <button
        v-if="editable"
        type="button"
        class="ghost h-10 w-fit rounded-full border border-line px-5 text-sm font-semibold text-red-700"
        @click="archive"
      >
        🗄 Archive card
      </button>
    </template>
  </SideSheet>
</template>
