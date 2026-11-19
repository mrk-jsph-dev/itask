<script setup lang="ts">
import { computed, reactive, ref, useId } from 'vue'

import { ApiError } from '@/shared/api/http'
import {
  automationsApi,
  type ActionSpec,
  type AutomationInput,
  type AutomationPreview,
  type ConditionSpec,
} from '../api'
import {
  actionOptions,
  aiActionOptions,
  clone,
  isAiAction,
  conditionOptions,
  describe,
  isHourly,
  priorities,
  triggerOptions,
  unfixable,
  type BoardRefs,
} from '../catalog'

const props = defineProps<{
  boardId: string
  initial: AutomationInput
  refs: BoardRefs
  saving?: boolean
  error?: string | null
  /** Offer ✨ AI actions (AI is on and the user isn't a demo account). */
  aiEnabled?: boolean
}>()
const emit = defineEmits<{ save: [input: AutomationInput]; cancel: [] }>()

const MAX_ACTIONS = 3
const MAX_CONDITIONS = 5

const draft = reactive<AutomationInput>(clone(props.initial))
draft.trigger ??= { type: 'card.completed' }
const id = useId()
const local = ref<string | null>(null)

const isDoorway = computed(() => draft.kind === 'Doorway')
const trigger = computed(() => triggerOptions.find((t) => t.value === draft.trigger!.type))
const editors = computed(() => props.refs.members.filter((m) => m.role !== 'Viewer'))
const sentence = computed(() => describe({ ...draft, trigger: draft.trigger! }, props.refs))
/** ✨ actions fit instant rules only, one per rule. */
const canUseAi = computed(
  () => !!props.aiEnabled && !isDoorway.value && !isHourly(draft.trigger!.type),
)
const hasAi = computed(() => draft.actions.some((a) => isAiAction(a.type)))
const aiOptionsFor = (a: ActionSpec) =>
  canUseAi.value && (!hasAi.value || isAiAction(a.type)) ? aiActionOptions : []

const preview = ref<AutomationPreview | null>(null)
const previewing = ref(false)
const previewError = ref<string | null>(null)

/** Replays the draft over the board's recent activity; nothing changes. */
async function replay() {
  previewing.value = true
  previewError.value = null
  try {
    preview.value = await automationsApi.preview(props.boardId, {
      ...clone(draft),
      trigger: isDoorway.value ? null : draft.trigger,
    })
  } catch (e) {
    preview.value = null
    previewError.value =
      e instanceof ApiError && e.errors
        ? Object.values(e.errors).flat().join(' ')
        : e instanceof Error
          ? e.message
          : "Couldn't replay it."
  } finally {
    previewing.value = false
  }
}

const firstList = () => props.refs.lists[0]?.id ?? null
const firstLabel = () => props.refs.labels[0]?.id ?? null

function onTriggerType() {
  const t = draft.trigger!
  const needs = trigger.value?.needs
  t.listId = needs === 'list' || needs === 'list+days' ? (t.listId ?? firstList()) : null
  t.labelId = needs === 'label' ? (t.labelId ?? firstLabel()) : null
  t.days =
    needs === 'days' || needs === 'list+days' ? (t.days ?? (t.type === 'due.soon' ? 2 : 3)) : null
}

function addCondition() {
  draft.conditions.push({ type: 'assignee.set' })
}

function onConditionType(c: ConditionSpec) {
  c.labelIds =
    c.type === 'label.any' || c.type === 'label.none' ? (firstLabel() ? [firstLabel()!] : []) : null
  c.priority = c.type === 'priority.atLeast' ? 'High' : null
  c.userId = c.type === 'assignee.is' ? (editors.value[0]?.userId ?? null) : null
}

function addAction() {
  draft.actions.push({ type: 'complete' })
}

function onActionType(a: ActionSpec) {
  a.listId = a.type === 'move' ? firstList() : null
  a.position = a.type === 'move' ? 'bottom' : null
  a.labelId = a.type === 'label.add' || a.type === 'label.remove' ? firstLabel() : null
  a.who = a.type === 'assign' ? 'actor' : a.type === 'notify' ? 'assignees' : null
  a.userId = null
  a.priority = a.type === 'priority' ? 'High' : null
  a.days = a.type === 'due' ? 3 : null
  a.items = a.type === 'checklist.add' ? [''] : null
  a.text = a.type === 'comment' ? '' : null
}

function itemsText(a: ActionSpec) {
  return (a.items ?? []).join('\n')
}

function setItems(a: ActionSpec, value: string) {
  a.items = value
    .split('\n')
    .map((s) => s.trim())
    .filter(Boolean)
    .slice(0, 10)
}

function save() {
  local.value = null
  if (!draft.name.trim()) {
    local.value = 'Give it a name.'
    return
  }
  if (!isDoorway.value && draft.actions.length === 0) {
    local.value = 'Add at least one action.'
    return
  }
  if (
    isDoorway.value &&
    draft.mode === 'Fix' &&
    draft.conditions.some((c) => unfixable.has(c.type))
  ) {
    local.value =
      "A due date or a finished checklist can't be filled in automatically. Use block or warn."
    return
  }
  emit('save', {
    ...clone(draft),
    name: draft.name.trim(),
    trigger: isDoorway.value ? null : draft.trigger,
  })
}

const field = 'h-9 min-w-0 rounded-[10px] border border-line bg-panel px-2.5 text-sm'
const chip = 'text-sm font-semibold text-muted'
</script>

<template>
  <form
    class="flex flex-col gap-4 rounded-[18px] border border-line-soft p-4"
    @submit.prevent="save"
  >
    <label class="flex flex-col gap-1.5 text-sm font-semibold">
      Name
      <input
        v-model="draft.name"
        type="text"
        maxlength="80"
        :class="field"
        placeholder="e.g. Review gate"
      />
    </label>

    <fieldset v-if="isDoorway" class="flex flex-wrap items-center gap-2">
      <legend class="mb-2 text-sm font-semibold">🚪 Doorway</legend>
      <select v-model="draft.direction" :class="field" aria-label="Direction">
        <option value="Enter">Coming into</option>
        <option value="Leave">Leaving</option>
      </select>
      <select v-model="draft.listId" :class="field" aria-label="List">
        <option v-for="l in refs.lists" :key="l.id" :value="l.id">{{ l.name }}</option>
      </select>
      <span :class="chip">needs the requirements below. Otherwise:</span>
      <select v-model="draft.mode" :class="field" aria-label="Mode">
        <option value="Block">block the move</option>
        <option value="Fix">fill in what's missing</option>
        <option value="Warn">let it through with a warning</option>
      </select>
    </fieldset>

    <fieldset v-else class="flex flex-wrap items-center gap-2">
      <legend class="mb-2 text-sm font-semibold">⚡ When</legend>
      <select
        v-model="draft.trigger!.type"
        :class="field"
        aria-label="Trigger"
        @change="onTriggerType"
      >
        <option v-for="t in triggerOptions" :key="t.value" :value="t.value">{{ t.label }}</option>
      </select>
      <select
        v-if="trigger?.needs === 'list' || trigger?.needs === 'list+days'"
        v-model="draft.trigger!.listId"
        :class="field"
        aria-label="Trigger list"
      >
        <option v-for="l in refs.lists" :key="l.id" :value="l.id">{{ l.name }}</option>
      </select>
      <select
        v-if="trigger?.needs === 'label'"
        v-model="draft.trigger!.labelId"
        :class="field"
        aria-label="Trigger label"
      >
        <option v-for="l in refs.labels" :key="l.id" :value="l.id">{{ l.name }}</option>
      </select>
      <template v-if="trigger?.needs === 'days' || trigger?.needs === 'list+days'">
        <span v-if="draft.trigger!.type === 'due.soon'" :class="chip">within</span>
        <span v-else-if="draft.trigger!.type === 'list.stale'" :class="chip">for</span>
        <input
          v-model.number="draft.trigger!.days"
          type="number"
          min="1"
          max="90"
          :class="[field, 'w-20']"
          aria-label="Days"
        />
        <span :class="chip">{{
          draft.trigger!.type === 'completed.aged' ? 'days ago' : 'days'
        }}</span>
      </template>
      <label
        v-if="draft.trigger!.type === 'card.created'"
        class="flex items-center gap-1.5 text-sm"
      >
        <span :class="chip">in</span>
        <select v-model="draft.trigger!.listId" :class="field" aria-label="Created in list">
          <option :value="null">any list</option>
          <option v-for="l in refs.lists" :key="l.id" :value="l.id">{{ l.name }}</option>
        </select>
      </label>
      <label
        v-if="draft.trigger!.type === 'comment.added'"
        class="flex items-center gap-1.5 text-sm"
      >
        <input v-model="draft.trigger!.mentions" type="checkbox" /> only when someone is mentioned
      </label>
    </fieldset>

    <fieldset class="flex flex-col gap-2">
      <legend class="mb-2 text-sm font-semibold">
        {{ isDoorway ? '✅ Requirements' : '🔎 Only if the card' }}
      </legend>
      <div v-for="(c, i) in draft.conditions" :key="i" class="flex flex-wrap items-center gap-2">
        <select
          v-model="c.type"
          :class="field"
          :aria-label="`Condition ${i + 1}`"
          @change="onConditionType(c)"
        >
          <option v-for="o in conditionOptions" :key="o.value" :value="o.value">
            {{ o.label }}
          </option>
        </select>
        <select
          v-if="c.type === 'label.any' || c.type === 'label.none'"
          :value="c.labelIds?.[0] ?? ''"
          :class="field"
          :aria-label="`Condition ${i + 1} label`"
          @change="c.labelIds = [($event.target as HTMLSelectElement).value]"
        >
          <option v-for="l in refs.labels" :key="l.id" :value="l.id">{{ l.name }}</option>
        </select>
        <select
          v-if="c.type === 'priority.atLeast'"
          v-model="c.priority"
          :class="field"
          :aria-label="`Condition ${i + 1} priority`"
        >
          <option v-for="p in priorities" :key="p" :value="p">{{ p }}</option>
        </select>
        <select
          v-if="c.type === 'assignee.is'"
          v-model="c.userId"
          :class="field"
          :aria-label="`Condition ${i + 1} member`"
        >
          <option v-for="m in refs.members" :key="m.userId" :value="m.userId">
            {{ m.displayName }}
          </option>
        </select>
        <button
          type="button"
          class="ghost h-8 w-8 rounded-full text-muted"
          :aria-label="`Remove condition ${i + 1}`"
          @click="draft.conditions.splice(i, 1)"
        >
          ✕
        </button>
      </div>
      <button
        v-if="draft.conditions.length < MAX_CONDITIONS"
        type="button"
        class="ghost w-fit rounded-full px-3 py-1 text-sm font-semibold text-accent"
        @click="addCondition"
      >
        + {{ isDoorway ? 'Requirement' : 'Condition' }}
      </button>
    </fieldset>

    <fieldset class="flex flex-col gap-2">
      <legend class="mb-2 text-sm font-semibold">
        {{ isDoorway ? '➡️ Also, every time' : '➡️ Then' }}
      </legend>
      <div v-for="(a, i) in draft.actions" :key="i" class="flex flex-wrap items-start gap-2">
        <select
          v-model="a.type"
          :class="field"
          :aria-label="`Action ${i + 1}`"
          @change="onActionType(a)"
        >
          <option v-for="o in actionOptions" :key="o.value" :value="o.value">{{ o.label }}</option>
          <optgroup v-if="aiOptionsFor(a).length" label="✨ AI (uses your AI quota)">
            <option v-for="o in aiOptionsFor(a)" :key="o.value" :value="o.value">
              {{ o.label }}
            </option>
          </optgroup>
        </select>
        <template v-if="a.type === 'move'">
          <select v-model="a.listId" :class="field" :aria-label="`Action ${i + 1} list`">
            <option v-for="l in refs.lists" :key="l.id" :value="l.id">{{ l.name }}</option>
          </select>
          <select v-model="a.position" :class="field" :aria-label="`Action ${i + 1} position`">
            <option value="bottom">at the bottom</option>
            <option value="top">at the top</option>
          </select>
        </template>
        <select
          v-if="a.type === 'label.add' || a.type === 'label.remove'"
          v-model="a.labelId"
          :class="field"
          :aria-label="`Action ${i + 1} label`"
        >
          <option v-for="l in refs.labels" :key="l.id" :value="l.id">{{ l.name }}</option>
        </select>
        <template v-if="a.type === 'assign'">
          <select v-model="a.who" :class="field" :aria-label="`Action ${i + 1} who`">
            <option value="actor">whoever did it</option>
            <option value="creator">the card's creator</option>
            <option value="member">a member…</option>
          </select>
          <select
            v-if="a.who === 'member'"
            v-model="a.userId"
            :class="field"
            :aria-label="`Action ${i + 1} member`"
          >
            <option v-for="m in editors" :key="m.userId" :value="m.userId">
              {{ m.displayName }}
            </option>
          </select>
        </template>
        <select
          v-if="a.type === 'priority'"
          v-model="a.priority"
          :class="field"
          :aria-label="`Action ${i + 1} priority`"
        >
          <option value="None">None</option>
          <option v-for="p in priorities" :key="p" :value="p">{{ p }}</option>
        </select>
        <label v-if="a.type === 'due'" class="flex items-center gap-1.5 text-sm">
          <span :class="chip">to</span>
          <input
            v-model.number="a.days"
            type="number"
            min="0"
            max="365"
            :class="[field, 'w-20']"
            :aria-label="`Action ${i + 1} days`"
          />
          <span :class="chip">days from now</span>
        </label>
        <textarea
          v-if="a.type === 'checklist.add'"
          :value="itemsText(a)"
          rows="3"
          maxlength="3000"
          class="min-w-0 flex-1 rounded-[10px] border border-line bg-panel px-2.5 py-2 text-sm"
          placeholder="One item per line"
          :aria-label="`Action ${i + 1} checklist items`"
          @change="setItems(a, ($event.target as HTMLTextAreaElement).value)"
        />
        <input
          v-if="a.type === 'comment'"
          v-model="a.text"
          type="text"
          maxlength="1000"
          :class="[field, 'flex-1']"
          placeholder="Comment text"
          :aria-label="`Action ${i + 1} comment`"
        />
        <template v-if="a.type === 'notify'">
          <select v-model="a.who" :class="field" :aria-label="`Action ${i + 1} recipients`">
            <option value="assignees">the assignees</option>
            <option value="owners">the owners</option>
            <option value="members">everyone on the board</option>
          </select>
          <input
            v-model="a.text"
            type="text"
            maxlength="200"
            :class="[field, 'flex-1']"
            placeholder="Note (optional)"
            :aria-label="`Action ${i + 1} note`"
          />
        </template>
        <button
          type="button"
          class="ghost h-8 w-8 rounded-full text-muted"
          :aria-label="`Remove action ${i + 1}`"
          @click="draft.actions.splice(i, 1)"
        >
          ✕
        </button>
      </div>
      <button
        v-if="draft.actions.length < MAX_ACTIONS"
        type="button"
        class="ghost w-fit rounded-full px-3 py-1 text-sm font-semibold text-accent"
        @click="addAction"
      >
        + Action
      </button>
    </fieldset>

    <p :id="`${id}-preview`" class="rounded-[12px] bg-hover px-3 py-2 text-sm">
      {{ sentence }}
      <span v-if="hasAi" class="ml-1 font-semibold text-violet-700">· ✨ uses AI quota</span>
      <span class="ml-1 text-muted">
        {{
          !isDoorway && isHourly(draft.trigger!.type)
            ? '· Hourly, may run late while the app sleeps'
            : '· Instant, free'
        }}
      </span>
    </p>
    <div class="flex flex-col gap-1.5">
      <button
        type="button"
        class="ghost w-fit rounded-full border border-line px-3 py-1 text-sm font-semibold"
        :disabled="previewing"
        @click="replay"
      >
        {{ previewing ? 'Replaying…' : '⏪ Replay before adding' }}
      </button>
      <p v-if="preview" class="text-sm" data-testid="automation-preview">
        <template v-if="preview.basis === 'history'">
          Would have {{ isDoorway ? 'stopped' : 'run' }} {{ preview.count }} time{{
            preview.count === 1 ? '' : 's'
          }}
          in the last 30 days<span class="text-muted"> (based on how the cards look now)</span>.
        </template>
        <template v-else>
          Would run on {{ preview.count }} card{{ preview.count === 1 ? '' : 's' }} now.
        </template>
        <span v-if="preview.samples.length" class="text-muted">
          e.g. {{ preview.samples.map((c) => `${c.key} ${c.title}`).join(', ') }}
        </span>
      </p>
      <p v-if="previewError" class="text-sm text-red-700">{{ previewError }}</p>
    </div>
    <p v-if="local || error" role="alert" class="text-sm text-red-700">{{ local ?? error }}</p>
    <div class="flex gap-2">
      <button
        type="submit"
        class="press h-9 rounded-full bg-accent px-4 text-sm font-semibold text-white disabled:opacity-60"
        :disabled="saving"
      >
        {{ saving ? 'Saving…' : 'Save' }}
      </button>
      <button
        type="button"
        class="ghost h-9 rounded-full px-4 text-sm font-semibold"
        @click="emit('cancel')"
      >
        Cancel
      </button>
    </div>
  </form>
</template>
