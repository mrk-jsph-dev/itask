<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, useTemplateRef, watch } from 'vue'

import CardActivity from '@/features/activity/components/CardActivity.vue'
import { useAuthStore } from '@/features/auth/store'
import SideSheet from '@/shared/ui/SideSheet.vue'
import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { useMenu } from '@/shared/ui/menu'
import { colorPalette } from '@/shared/ui/palette'
import { toast } from '@/shared/ui/toast'
import type { CardPatch, CardPriority } from '../api'
import { useCardDetailStore } from '../cardDetailStore'
import { useCardsStore } from '../cardsStore'
import { fromDateInput, priorities, priorityStyle, toDateInput } from '../cardOptions'
import { cardEmojis } from '../options'
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

/** Below the attachments: comments with the composer, the card's activity, or both mixed. */
const timelineTabs = [
  { value: 'comments', label: 'Comments' },
  { value: 'activity', label: '⚡ Activity' },
  { value: 'all', label: 'All' },
] as const
const timeline = ref<(typeof timelineTabs)[number]['value']>('comments')
const timelineEl = useTemplateRef('timelineEl')

/** "View task history": the card's activity, scrolled into view. */
async function showHistory() {
  timeline.value = 'activity'
  await nextTick()
  timelineEl.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

const emojiMenu = useTemplateRef('emojiMenu')
const emojiTrigger = useTemplateRef('emojiTrigger')
const emojiPicker = useMenu(emojiMenu, emojiTrigger)
const emojiOpen = emojiPicker.open
const labelMenu = useTemplateRef('labelMenu')
const labelTrigger = useTemplateRef('labelTrigger')
const labelPicker = useMenu(labelMenu, labelTrigger)
const labelOpen = labelPicker.open
const assigneeMenu = useTemplateRef('assigneeMenu')
const assigneeTrigger = useTemplateRef('assigneeTrigger')
const assigneePicker = useMenu(assigneeMenu, assigneeTrigger)
const assigneeOpen = assigneePicker.open

const priorityPill = computed(() =>
  card.value && card.value.priority !== 'None' ? priorityStyle[card.value.priority] : null,
)

function pickEmoji(emoji: string | null) {
  emojiPicker.close(true)
  void save({ emoji })
}

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

const dateClass =
  'h-9 rounded-[10px] border border-transparent bg-transparent px-2 font-medium hover:border-line focus:border-line disabled:hover:border-transparent'
</script>

<template>
  <SideSheet :title="card ? `${card.key} ${card.title}` : 'Card'" wide @close="emit('close')">
    <template #header>
      <div
        class="flex flex-wrap items-center gap-2.5 border-b border-[#EEF1F6] px-5 py-[18px] md:px-6"
      >
        <p v-if="card && spot" class="min-w-0 text-sm text-muted">
          {{ boards.current?.name }} / {{ spot.list.name }}
          <span class="ml-1 font-semibold tracking-wide">{{ card.key }}</span>
        </p>
        <button
          v-if="card"
          type="button"
          class="press ml-auto flex h-[38px] items-center gap-1.5 rounded-full border px-3.5 text-sm font-medium disabled:opacity-60"
          :class="
            card.completedAt
              ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
              : 'border-line bg-panel'
          "
          :disabled="!editable"
          :aria-pressed="!!card.completedAt"
          @click="cards.toggleComplete(card)"
        >
          <span v-if="card.completedAt" class="pop" aria-hidden="true">✓</span>
          {{ card.completedAt ? 'Completed' : 'Mark complete' }}
        </button>
        <button
          type="button"
          class="ghost flex h-[38px] w-[38px] items-center justify-center rounded-full bg-hover"
          :class="{ 'ml-auto': !card }"
          aria-label="Close task"
          @click="emit('close')"
        >
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="#374151"
            stroke-width="2.2"
            stroke-linecap="round"
            aria-hidden="true"
          >
            <path d="M6 6l12 12M18 6L6 18" />
          </svg>
        </button>
      </div>
    </template>

    <div v-if="card && spot" class="flex flex-col gap-6 p-5 md:p-6">
      <div class="relative flex items-start gap-2">
        <button
          ref="emojiTrigger"
          type="button"
          class="ghost flex h-11 w-11 shrink-0 items-center justify-center rounded-xl text-[26px] disabled:cursor-default"
          :class="{ 'border border-dashed border-line text-base text-muted': !card.emoji }"
          :disabled="!editable"
          aria-haspopup="menu"
          :aria-expanded="emojiOpen"
          :aria-label="card.emoji ? `Card emoji ${card.emoji}, change` : 'Add card emoji'"
          @click.stop="emojiPicker.toggle()"
        >
          <span aria-hidden="true">{{ card.emoji ?? '☺︎' }}</span>
        </button>
        <div
          v-if="emojiOpen"
          ref="emojiMenu"
          role="menu"
          aria-label="Card emoji"
          class="pop absolute top-12 left-0 z-30 grid w-[248px] grid-cols-6 gap-1 rounded-[var(--radius-control)] border border-line-soft bg-panel p-2 shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
          @keydown="emojiPicker.onKeydown"
        >
          <button
            v-for="option in cardEmojis"
            :key="option"
            type="button"
            role="menuitemradio"
            :aria-checked="card.emoji === option"
            :aria-label="`Emoji ${option}`"
            class="ghost flex h-9 w-9 items-center justify-center rounded-lg text-xl"
            :class="{ 'bg-hover ring-2 ring-accent': card.emoji === option }"
            @click="pickEmoji(option)"
          >
            {{ option }}
          </button>
          <button
            type="button"
            role="menuitemradio"
            :aria-checked="!card.emoji"
            class="ghost col-span-6 h-8 rounded-lg text-sm text-muted"
            @click="pickEmoji(null)"
          >
            No emoji
          </button>
        </div>
        <label class="flex min-w-0 flex-1 flex-col">
          <span class="sr-only">Title</span>
          <textarea
            v-model="title"
            rows="1"
            maxlength="200"
            :disabled="!editable"
            class="field-sizing-content resize-none rounded-[var(--radius-control)] border border-transparent px-2 py-1 text-[26px] leading-tight font-bold tracking-[-0.02em] hover:border-line focus:border-line disabled:bg-transparent"
            :class="{ 'text-muted line-through': card.completedAt }"
            @keydown.enter.prevent="($event.target as HTMLTextAreaElement).blur()"
            @blur="saveTitle"
          />
        </label>
      </div>

      <dl
        class="grid grid-cols-[96px_minmax(0,1fr)] items-center gap-x-4 gap-y-3.5 text-sm sm:grid-cols-[120px_minmax(0,1fr)]"
      >
        <dt class="text-muted">Status</dt>
        <dd class="inline-flex items-center gap-1.5 font-semibold">
          <span aria-hidden="true">{{ spot.list.emoji }}</span
          >{{ spot.list.name }}
          <span v-if="card.completedAt" class="font-normal text-emerald-700">· Done</span>
        </dd>

        <dt class="text-muted"><label :for="`priority-${card.id}`">Priority</label></dt>
        <dd>
          <select
            :id="`priority-${card.id}`"
            :value="card.priority"
            :disabled="!editable"
            class="h-[30px] rounded-lg border-0 px-2.5 text-[13px] font-semibold disabled:opacity-100"
            :style="
              priorityPill
                ? { color: priorityPill.color, backgroundColor: priorityPill.bg }
                : { backgroundColor: '#F3F6FB' }
            "
            @change="onPriority"
          >
            <option v-for="p in priorities" :key="p" :value="p">
              {{ p === 'None' ? 'No priority' : p }}
            </option>
          </select>
        </dd>

        <dt class="text-muted"><label :for="`due-${card.id}`">Due date</label></dt>
        <dd>
          <input
            :id="`due-${card.id}`"
            type="date"
            :value="toDateInput(card.dueAt)"
            :disabled="!editable"
            :class="dateClass"
            @change="onDate('dueAt', $event)"
          />
        </dd>

        <dt class="text-muted"><label :for="`start-${card.id}`">Start date</label></dt>
        <dd>
          <input
            :id="`start-${card.id}`"
            type="date"
            :value="toDateInput(card.startAt)"
            :disabled="!editable"
            :class="dateClass"
            @change="onDate('startAt', $event)"
          />
        </dd>

        <dt class="text-muted">Label</dt>
        <dd class="relative flex flex-wrap gap-1.5">
          <span
            v-for="label in card.labels"
            :key="label.id"
            class="inline-flex items-center gap-1.5 rounded-full border border-line-soft px-2.5 py-[3px] text-[13px]"
          >
            <span v-if="label.emoji" aria-hidden="true">{{ label.emoji }}</span>
            <span
              v-else
              class="h-2 w-2 rounded-full"
              :style="{ backgroundColor: label.color }"
              aria-hidden="true"
            />
            {{ label.name }}
          </span>
          <span v-if="!card.labels.length && !editable" class="text-muted">None</span>
          <button
            v-if="editable"
            ref="labelTrigger"
            type="button"
            class="ghost h-7 rounded-full border border-dashed border-line bg-panel px-2.5 text-[13px] text-[#374151]"
            aria-haspopup="menu"
            :aria-expanded="labelOpen"
            aria-label="Edit labels"
            @click.stop="labelPicker.toggle()"
          >
            + Add
          </button>
          <div
            v-if="labelOpen"
            ref="labelMenu"
            role="menu"
            aria-label="Labels"
            class="pop absolute top-9 left-0 z-30 flex w-56 flex-col rounded-[var(--radius-control)] border border-line-soft bg-panel p-1 shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
            @keydown="labelPicker.onKeydown"
          >
            <button
              v-for="label in cards.labels ?? []"
              :key="label.id"
              type="button"
              role="menuitemcheckbox"
              :aria-checked="card.labels.some((l) => l.id === label.id)"
              class="ghost flex items-center gap-2 rounded-lg px-3 py-2 text-left text-sm"
              @click="toggleLabel(label.id)"
            >
              <span class="w-4 text-accent" aria-hidden="true">{{
                card.labels.some((l) => l.id === label.id) ? '✓' : ''
              }}</span>
              <span v-if="label.emoji" aria-hidden="true">{{ label.emoji }}</span>
              <span
                v-else
                class="h-2 w-2 rounded-full"
                :style="{ backgroundColor: label.color }"
                aria-hidden="true"
              />
              {{ label.name }}
            </button>
            <p v-if="cards.labels?.length === 0" class="px-3 py-2 text-sm text-muted">
              No labels yet. Add some in board settings.
            </p>
          </div>
        </dd>

        <dt class="text-muted">Assignees</dt>
        <dd class="relative flex flex-wrap items-center gap-1.5">
          <span
            v-for="person in card.assignees"
            :key="person.userId"
            :title="person.displayName"
            class="flex"
          >
            <UserAvatar :name="person.displayName" :color="person.avatarColor" :size="30" />
            <span class="sr-only">{{ person.displayName }}</span>
          </span>
          <span v-if="!card.assignees.length && !editable" class="text-muted">Nobody</span>
          <button
            v-if="editable"
            ref="assigneeTrigger"
            type="button"
            class="ghost flex h-[30px] w-[30px] items-center justify-center rounded-full border border-dashed border-line text-[#374151]"
            aria-haspopup="menu"
            :aria-expanded="assigneeOpen"
            aria-label="Edit assignees"
            @click.stop="assigneePicker.toggle()"
          >
            +
          </button>
          <div
            v-if="assigneeOpen"
            ref="assigneeMenu"
            role="menu"
            aria-label="Assignees"
            class="pop absolute top-10 left-0 z-30 flex w-60 flex-col rounded-[var(--radius-control)] border border-line-soft bg-panel p-1 shadow-[0_10px_24px_rgb(17_24_39/0.12)]"
            @keydown="assigneePicker.onKeydown"
          >
            <button
              v-for="member in boards.members"
              :key="member.userId"
              type="button"
              role="menuitemcheckbox"
              :aria-checked="card.assignees.some((a) => a.userId === member.userId)"
              class="ghost flex items-center gap-2.5 rounded-lg px-3 py-1.5 text-left text-sm"
              @click="toggleAssignee(member.userId)"
            >
              <span class="w-4 text-accent" aria-hidden="true">{{
                card.assignees.some((a) => a.userId === member.userId) ? '✓' : ''
              }}</span>
              <UserAvatar
                :name="member.displayName"
                :email="member.email"
                :color="member.avatarColor"
                :size="26"
              />
              <span class="truncate">{{ member.displayName || member.email }}</span>
            </button>
          </div>
        </dd>

        <template v-if="editable || card.coverColor">
          <dt class="text-muted">Cover</dt>
          <dd class="flex flex-wrap gap-1.5" role="group" aria-label="Cover colour">
            <button
              v-if="editable"
              type="button"
              class="flex h-7 w-7 items-center justify-center rounded-lg border border-line text-xs text-muted"
              :class="{ 'ring-2 ring-accent': !card.coverColor }"
              :aria-pressed="!card.coverColor"
              aria-label="No cover"
              @click="save({ coverColor: null })"
            >
              ✕
            </button>
            <button
              v-for="color in editable ? colorPalette : [card.coverColor!]"
              :key="color"
              type="button"
              class="h-7 w-7 rounded-lg disabled:cursor-default"
              :class="{ 'ring-2 ring-accent ring-offset-2': card.coverColor === color }"
              :style="{ backgroundColor: color }"
              :disabled="!editable"
              :aria-pressed="card.coverColor === color"
              :aria-label="`Cover ${color}`"
              @click="save({ coverColor: color })"
            />
          </dd>
        </template>
      </dl>

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
        <section
          ref="timelineEl"
          class="flex scroll-mt-4 flex-col gap-3"
          aria-labelledby="timeline-title"
        >
          <div class="flex flex-wrap items-center gap-2.5">
            <h2 id="timeline-title" class="text-base font-semibold">💬 Comments</h2>
            <div
              role="tablist"
              aria-label="Card timeline"
              class="ml-auto flex rounded-[12px] bg-hover p-1"
            >
              <button
                v-for="tab in timelineTabs"
                :key="tab.value"
                type="button"
                role="tab"
                :aria-selected="timeline === tab.value"
                class="h-8 rounded-[10px] px-3 text-sm"
                :class="
                  timeline === tab.value
                    ? 'bg-panel font-semibold text-ink shadow-[0_1px_2px_rgb(17_24_39/0.08)]'
                    : 'font-medium text-muted'
                "
                @click="timeline = tab.value"
              >
                {{ tab.label }}
              </button>
            </div>
          </div>
          <CardComments v-if="timeline === 'comments'" :can-post="canComment" />
          <CardActivity
            v-else
            :board-id="lists.boardId!"
            :card-id="cardId"
            :comments="timeline === 'all' ? detail.comments : undefined"
          />
        </section>
      </template>

      <div class="flex flex-wrap items-center gap-2.5 border-t border-[#EEF1F6] pt-4">
        <button
          type="button"
          class="flex min-h-11 items-center text-sm font-semibold text-accent-strong"
          @click="showHistory"
        >
          View task history
        </button>
        <button
          v-if="editable"
          type="button"
          class="ghost ml-auto h-[42px] rounded-full border border-[#FECACA] bg-panel px-4 text-sm font-semibold text-[#B91C1C]"
          @click="archive"
        >
          🗄 Archive task
        </button>
      </div>
    </div>
  </SideSheet>
</template>
