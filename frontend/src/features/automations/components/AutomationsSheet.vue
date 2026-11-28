<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

import { ApiError } from '@/shared/api/http'
import SideSheet from '@/shared/ui/SideSheet.vue'
import { toast, toastWithUndo } from '@/shared/ui/toast'
import { useAiStore } from '@/features/ai/store'
import {
  automationsApi,
  type AiSuggestion,
  type Automation,
  type AutomationInput,
  type AutomationSummary,
} from '../api'
import { blankRule, clone, describe, presetsFor, usesAi, type BoardRefs } from '../catalog'
import { useAutomationsStore } from '../store'
import RuleEditor from './RuleEditor.vue'

const props = defineProps<{
  boardId: string
  refs: BoardRefs
  /** Owners add, change and remove automations; others only read them. */
  canManage: boolean
  /** ✨ Describe it, Review my board and AI actions (AI is on and not a demo account). */
  aiEnabled?: boolean
  /** A draft to open in the editor at once (from "Show me once"). */
  draft?: AutomationInput | null
}>()
const emit = defineEmits<{ close: []; record: [] }>()

const ai = useAiStore()
const summary = ref<AutomationSummary | null>(null)
const describeText = ref('')
const suggestions = ref<AiSuggestion[] | null>(null)

const store = useAutomationsStore()
/** The automation being edited: its id, or `new` with the input it starts from. */
const editing = ref<{ id: string | 'new'; input: AutomationInput } | null>(null)
const saving = ref(false)
const error = ref<string | null>(null)
/** Hidden while their delete waits out the Undo window. */
const hidden = ref(new Set<string>())

onMounted(() => {
  // Always reload: run counts change with every card change on the board.
  void store.load(props.boardId).catch(() => undefined)
  automationsApi
    .summary(props.boardId)
    .then((s) => (summary.value = s))
    .catch(() => undefined)
  if (props.draft) editing.value = { id: 'new', input: clone(props.draft) }
})

async function describeIt() {
  const text = describeText.value.trim()
  if (!text) return
  const result = await ai.run('automation-draft', () => automationsApi.aiDraft(props.boardId, text))
  if (!result) return
  describeText.value = ''
  error.value = null
  editing.value = { id: 'new', input: result.automation }
}

async function reviewBoard() {
  const result = await ai.run('automation-review', () => automationsApi.aiReview(props.boardId))
  if (result) suggestions.value = result.suggestions
}

function useSuggestion(s: AiSuggestion) {
  error.value = null
  editing.value = { id: 'new', input: clone(s.automation) }
}

const items = computed(() => (store.items ?? []).filter((a) => !hidden.value.has(a.id)))
const doorways = computed(() => items.value.filter((a) => a.kind === 'Doorway'))
const rules = computed(() => items.value.filter((a) => a.kind === 'Rule'))
const presets = computed(() => (props.canManage ? presetsFor(props.refs, items.value) : []))

function message(e: unknown) {
  if (e instanceof ApiError && e.errors) return Object.values(e.errors).flat().join(' ')
  return e instanceof Error ? e.message : 'Something went wrong.'
}

function startNew(kind: 'Rule' | 'Doorway') {
  error.value = null
  const input = blankRule()
  if (kind === 'Doorway') {
    Object.assign(input, {
      kind,
      listId: props.refs.lists[0]?.id ?? null,
      direction: 'Enter',
      mode: 'Block',
      trigger: null,
      conditions: [{ type: 'assignee.set' }],
      actions: [],
    })
  }
  editing.value = { id: 'new', input }
}

function startEdit(a: Automation) {
  error.value = null
  editing.value = {
    id: a.id,
    input: {
      kind: a.kind,
      name: a.name,
      listId: a.listId,
      direction: a.direction,
      mode: a.mode,
      trigger: a.trigger,
      conditions: a.conditions,
      actions: a.actions,
    },
  }
}

async function save(input: AutomationInput) {
  if (!editing.value) return
  saving.value = true
  error.value = null
  try {
    if (editing.value.id === 'new') await store.create(input)
    else await store.update(editing.value.id, input)
    editing.value = null
  } catch (e) {
    error.value = message(e)
  } finally {
    saving.value = false
  }
}

async function addPreset(input: AutomationInput) {
  try {
    await store.create(input)
    toast(`Added ${input.name}.`)
  } catch (e) {
    toast(message(e), 'error')
  }
}

async function toggle(a: Automation) {
  try {
    await store.toggle(a.id, !a.enabled)
  } catch (e) {
    toast(message(e), 'error')
  }
}

function remove(a: Automation) {
  hidden.value = new Set([...hidden.value, a.id])
  let undone = false
  toastWithUndo(`Deleted ${a.name}.`, () => {
    undone = true
    hidden.value = new Set([...hidden.value].filter((id) => id !== a.id))
  })
  setTimeout(() => {
    if (undone) return
    store.remove(a.id).catch((e: unknown) => {
      hidden.value = new Set([...hidden.value].filter((id) => id !== a.id))
      toast(message(e), 'error')
    })
  }, 5000)
}

/** "3 runs, 2 blocked in 30 days"; block doorways mostly block, so both are shown. */
function activity(a: Automation) {
  const parts = [`${a.runs} run${a.runs === 1 ? '' : 's'}`]
  if (a.blocked || a.mode === 'Block') parts.push(`${a.blocked} blocked`)
  return `${parts.join(', ')} in 30 days`
}

const cost = (a: Automation) =>
  a.timing === 'hourly' ? 'Hourly · may run late while the app sleeps' : 'Instant · free'
</script>

<template>
  <SideSheet title="⚡ Automations" @close="emit('close')">
    <p class="text-sm text-muted">
      Doorways guard a list's entrance or exit. Rules react to changes. They run inside the change
      that sets them off, so they cost nothing, unless you add a ✨ AI action.
    </p>
    <p
      v-if="summary && (summary.ran || summary.blocked)"
      class="rounded-[12px] bg-hover px-3 py-2 text-sm"
      data-testid="automation-summary"
    >
      🧾 This week: {{ summary.sentence }}
    </p>

    <RuleEditor
      v-if="editing"
      :key="editing.id"
      :board-id="boardId"
      :initial="editing.input"
      :refs="refs"
      :ai-enabled="aiEnabled"
      :saving="saving"
      :error="error"
      @save="save"
      @cancel="editing = null"
    />

    <div v-else-if="canManage" class="flex flex-wrap gap-2">
      <button
        type="button"
        class="press h-9 rounded-full bg-accent px-4 text-sm font-semibold text-on-accent"
        @click="startNew('Doorway')"
      >
        🚪 New doorway
      </button>
      <button
        type="button"
        class="ghost h-9 rounded-full border border-line px-4 text-sm font-semibold"
        @click="startNew('Rule')"
      >
        ⚡ New rule
      </button>
      <button
        type="button"
        class="ghost h-9 rounded-full border border-line px-4 text-sm font-semibold"
        @click="emit('record')"
      >
        ⏺ Show me once
      </button>
    </div>

    <section v-if="canManage && aiEnabled && !editing" class="flex flex-col gap-2">
      <form class="flex gap-2" @submit.prevent="describeIt">
        <input
          v-model="describeText"
          type="text"
          maxlength="500"
          class="h-9 min-w-0 flex-1 rounded-[10px] border border-line bg-panel px-3 text-sm"
          placeholder="✨ Describe it: when a bug is done, move it to Done…"
          aria-label="Describe an automation"
        />
        <button
          type="submit"
          class="ghost h-9 rounded-full border border-line px-3 text-sm font-semibold disabled:opacity-60"
          :disabled="!!ai.busy || !describeText.trim()"
        >
          {{ ai.busy === 'automation-draft' ? 'Drafting…' : 'Draft' }}
        </button>
      </form>
      <button
        type="button"
        class="ghost w-fit rounded-full px-3 py-1 text-sm font-semibold text-ai disabled:opacity-60"
        :disabled="!!ai.busy"
        @click="reviewBoard"
      >
        {{ ai.busy === 'automation-review' ? 'Reviewing…' : '✨ Review my board' }}
      </button>
      <p class="text-xs text-muted">
        Uses your AI quota ({{ ai.remaining }} left today). Nothing is saved until you add it.
      </p>
      <ul v-if="suggestions?.length" class="flex flex-col gap-2" aria-label="Suggestions">
        <li
          v-for="(sg, i) in suggestions"
          :key="i"
          class="flex flex-col gap-1.5 rounded-[14px] border border-ai-line bg-ai-soft p-3"
        >
          <p class="text-sm font-semibold">{{ sg.automation.name }}</p>
          <p class="text-sm">{{ describe(sg.automation, refs) }}</p>
          <p class="text-xs text-muted">✨ {{ sg.reason }}</p>
          <button
            type="button"
            class="ghost w-fit rounded-full border border-line px-3 py-1 text-xs font-semibold"
            @click="useSuggestion(sg)"
          >
            Review and add
          </button>
        </li>
      </ul>
    </section>

    <p v-if="store.items === null" class="text-sm text-muted">Loading…</p>

    <section
      v-for="group in [
        { title: '🚪 Doorways', items: doorways },
        { title: '⚡ Rules', items: rules },
      ]"
      :key="group.title"
      class="flex flex-col gap-2"
    >
      <template v-if="group.items.length">
        <h3 class="text-sm font-semibold">{{ group.title }}</h3>
        <ul class="flex flex-col gap-2" :aria-label="group.title.slice(3)">
          <li
            v-for="a in group.items"
            :key="a.id"
            class="flex flex-col gap-2 rounded-[16px] border border-line-soft p-3"
            :class="{ 'opacity-60': !a.enabled }"
          >
            <div class="flex items-start gap-2">
              <div class="min-w-0 flex-1">
                <p class="font-semibold">{{ a.name }}</p>
                <p class="text-sm text-ink-soft">{{ describe(a, refs) }}</p>
              </div>
              <label
                v-if="canManage"
                class="flex items-center gap-1.5 text-xs font-semibold text-muted"
              >
                <input
                  type="checkbox"
                  :checked="a.enabled"
                  :aria-label="`${a.name} on`"
                  @change="toggle(a)"
                />
                On
              </label>
            </div>
            <div class="flex flex-wrap items-center gap-2 text-xs">
              <span
                class="rounded-full px-2 py-0.5 font-semibold"
                :class="
                  a.timing === 'hourly'
                    ? 'bg-warn-soft text-warn'
                    : 'bg-success-soft text-success'
                "
                >{{ cost(a) }}</span
              >
              <span
                v-if="usesAi(a)"
                class="rounded-full bg-ai-soft px-2 py-0.5 font-semibold text-ai"
                >✨ uses AI quota</span
              >
              <span class="text-muted" data-testid="automation-runs">{{ activity(a) }}</span>
              <template v-if="canManage">
                <button
                  type="button"
                  class="ghost ml-auto rounded-full px-2 py-0.5 font-semibold"
                  @click="startEdit(a)"
                >
                  Edit
                </button>
                <button
                  type="button"
                  class="ghost rounded-full px-2 py-0.5 font-semibold text-danger"
                  @click="remove(a)"
                >
                  Delete
                </button>
              </template>
            </div>
            <p v-if="a.problem" class="text-xs text-danger">⚠ {{ a.problem }}</p>
          </li>
        </ul>
      </template>
    </section>

    <p v-if="store.items?.length === 0 && !editing" class="text-sm text-muted">
      🌱 No automations yet.{{ canManage ? ' Start with one below.' : '' }}
    </p>

    <section v-if="presets.length && !editing" class="flex flex-col gap-2">
      <h3 class="text-sm font-semibold">Quick start</h3>
      <ul class="flex flex-col gap-1.5" aria-label="Ready-made automations">
        <li
          v-for="p in presets"
          :key="p.preset.id"
          class="flex items-center gap-2 rounded-[12px] px-2 py-1.5 hover:bg-hover"
        >
          <span aria-hidden="true">{{ p.preset.emoji }}</span>
          <span class="min-w-0 flex-1 text-sm">{{ p.preset.title }}</span>
          <button
            type="button"
            class="ghost rounded-full border border-line px-3 py-1 text-xs font-semibold"
            :aria-label="`Add ${p.input.name}`"
            @click="addPreset(p.input)"
          >
            Add
          </button>
        </li>
      </ul>
    </section>
  </SideSheet>
</template>
