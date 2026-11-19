<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import SideSheet from '@/shared/ui/SideSheet.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import { toast } from '@/shared/ui/toast'
import { useAuthStore } from '@/features/auth/store'
import BoardDetailsFields from './BoardDetailsFields.vue'
import LabelManager from './LabelManager.vue'
import { boardRoles, templatesApi, type BoardRole } from '../api'
import { inviteLink } from '../options'
import { errorText, useBoardsStore } from '../store'

const emit = defineEmits<{ close: [] }>()

const store = useBoardsStore()
const auth = useAuthStore()
const router = useRouter()

const board = computed(() => store.current!)
const myId = computed(() => auth.user?.id)

const name = ref(board.value.name)
const emoji = ref(board.value.emoji)
const description = ref(board.value.description ?? '')
const color = ref(board.value.color)
const keyPrefix = ref(board.value.keyPrefix)
const canEditLabels = computed(() => board.value.role !== 'Viewer' && !board.value.archivedAt)
const savingDetails = ref(false)
const detailsMessage = ref<{ ok: boolean; text: string } | null>(null)

async function saveDetails() {
  savingDetails.value = true
  detailsMessage.value = null
  try {
    await store.saveDetails({
      name: name.value.trim(),
      emoji: emoji.value,
      description: description.value?.trim() || null,
      color: color.value,
      keyPrefix: keyPrefix.value.trim().toUpperCase(),
    })
    detailsMessage.value = { ok: true, text: 'Board saved.' }
  } catch (e) {
    detailsMessage.value = { ok: false, text: errorText(e) }
  } finally {
    savingDetails.value = false
  }
}

async function changeRole(userId: string, event: Event) {
  const select = event.target as HTMLSelectElement
  const previous = store.members.find((m) => m.userId === userId)?.role
  try {
    await store.changeRole(userId, select.value as BoardRole)
  } catch (e) {
    if (previous) select.value = previous
    toast(errorText(e), 'error')
  }
}

async function removeMember(userId: string) {
  try {
    await store.removeMember(userId)
  } catch (e) {
    toast(errorText(e), 'error')
  }
}

async function leave() {
  try {
    await store.leave(board.value.id, myId.value!)
    emit('close')
    toast('You left the board.')
    await router.replace({ name: 'home' })
  } catch (e) {
    toast(errorText(e), 'error')
  }
}

const inviteEmail = ref('')
const inviteRole = ref<BoardRole>('Editor')
const inviting = ref(false)
const createdLink = ref<string | null>(null)
const inviteError = ref<string | null>(null)

async function createInvite() {
  inviting.value = true
  inviteError.value = null
  createdLink.value = null
  try {
    const invite = await store.createInvite(inviteRole.value, inviteEmail.value.trim() || null)
    createdLink.value = inviteLink(invite.token)
    inviteEmail.value = ''
  } catch (e) {
    inviteError.value = errorText(e)
  } finally {
    inviting.value = false
  }
}

async function copyLink() {
  if (!createdLink.value) return
  try {
    await navigator.clipboard.writeText(createdLink.value)
    toast('Invite link copied.')
  } catch {
    toast('Copy failed. Select the link and copy it.', 'error')
  }
}

async function revoke(inviteId: string) {
  try {
    await store.revokeInvite(inviteId)
  } catch (e) {
    toast(errorText(e), 'error')
  }
}

const confirmDelete = ref(false)

const templateName = ref(`${board.value.name} template`)
const templateCards = ref(true)
const savingTemplate = ref(false)
const templateError = ref<string | null>(null)

async function saveTemplate() {
  savingTemplate.value = true
  templateError.value = null
  try {
    const saved = await templatesApi.save(board.value.id, {
      name: templateName.value.trim(),
      description: null,
      includeCards: templateCards.value,
    })
    toast(`Saved “${saved.name}”. Pick it under New board.`)
  } catch (e) {
    templateError.value = errorText(e, "Couldn't save the template.")
  } finally {
    savingTemplate.value = false
  }
}

async function archiveOrRestore() {
  try {
    if (board.value.archivedAt) {
      await store.restore(board.value.id)
      toast('Board restored.')
    } else {
      await store.archive(board.value.id)
      toast('Board archived.')
    }
  } catch (e) {
    toast(errorText(e), 'error')
  }
}

async function deleteBoard() {
  if (!confirmDelete.value) {
    confirmDelete.value = true
    return
  }
  try {
    await store.remove(board.value.id)
    emit('close')
    toast('Board deleted.')
    await router.replace({ name: 'home' })
  } catch (e) {
    toast(errorText(e), 'error')
  }
}

const expires = (iso: string) =>
  new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })

const inputClass =
  'h-[46px] rounded-[var(--radius-control)] border border-line bg-panel px-3.5 text-[15px] font-normal'
const sectionClass = 'flex flex-col gap-4 rounded-[20px] border border-line-soft p-5'
</script>

<template>
  <SideSheet title="Board settings" @close="emit('close')">
    <form
      v-if="store.isOwner"
      :class="sectionClass"
      aria-labelledby="settings-details"
      @submit.prevent="saveDetails"
    >
      <h3 id="settings-details" class="text-base font-semibold">Details</h3>
      <BoardDetailsFields
        v-model:name="name"
        v-model:emoji="emoji"
        v-model:description="description"
        v-model:color="color"
      />
      <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
        Card key prefix
        <input
          v-model="keyPrefix"
          type="text"
          required
          minlength="2"
          maxlength="5"
          pattern="[A-Za-z]{2,5}"
          autocomplete="off"
          :class="[inputClass, 'uppercase']"
          aria-describedby="settings-key-hint"
        />
        <span id="settings-key-hint" class="text-xs font-normal text-muted">
          2 to 5 letters. Cards read {{ keyPrefix.toUpperCase() || 'ITK' }}-1, -2, …
        </span>
      </label>
      <p
        v-if="detailsMessage"
        role="status"
        class="text-sm"
        :class="detailsMessage.ok ? 'text-emerald-700' : 'text-red-600'"
      >
        {{ detailsMessage.text }}
      </p>
      <button
        type="submit"
        :disabled="savingDetails || !name.trim()"
        class="press h-11 w-fit rounded-full bg-accent px-6 text-sm font-semibold text-white disabled:opacity-60"
      >
        {{ savingDetails ? 'Saving…' : 'Save details' }}
      </button>
    </form>

    <LabelManager v-if="canEditLabels" />

    <section :class="sectionClass" aria-labelledby="settings-members">
      <h3 id="settings-members" class="text-base font-semibold">
        Members <span class="text-muted">{{ store.members.length }}</span>
      </h3>
      <ul class="flex flex-col gap-3">
        <li v-for="member in store.members" :key="member.userId" class="flex items-center gap-3">
          <UserAvatar
            :name="member.displayName"
            :email="member.email"
            :color="member.avatarColor"
            :size="32"
          />
          <div class="flex min-w-0 flex-1 flex-col">
            <span class="truncate text-sm font-semibold">
              {{ member.displayName || member.email }}
              <span v-if="member.userId === myId" class="font-normal text-muted">(you)</span>
            </span>
            <span class="truncate text-xs text-muted">{{ member.email }}</span>
          </div>
          <template v-if="store.isOwner">
            <select
              :value="member.role"
              class="h-9 rounded-[10px] border border-line bg-panel px-2 text-sm"
              :aria-label="`Role for ${member.displayName || member.email}`"
              @change="changeRole(member.userId, $event)"
            >
              <option v-for="role in boardRoles" :key="role" :value="role">{{ role }}</option>
            </select>
            <button
              v-if="member.userId !== myId"
              type="button"
              class="ghost flex h-9 w-9 items-center justify-center rounded-full text-muted"
              :aria-label="`Remove ${member.displayName || member.email}`"
              @click="removeMember(member.userId)"
            >
              ✕
            </button>
          </template>
          <span v-else class="text-xs font-semibold text-muted">{{ member.role }}</span>
        </li>
      </ul>
    </section>

    <form
      v-if="store.isOwner"
      :class="sectionClass"
      aria-labelledby="settings-invite"
      @submit.prevent="createInvite"
    >
      <h3 id="settings-invite" class="text-base font-semibold">Invite people</h3>
      <p class="-mt-2 text-xs text-muted">
        Leave the email empty for a link anyone can use once. Links expire after 7 days.
      </p>
      <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
        Email (optional)
        <input
          v-model="inviteEmail"
          type="email"
          autocomplete="off"
          maxlength="256"
          :class="inputClass"
        />
      </label>
      <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
        Role
        <select v-model="inviteRole" :class="inputClass">
          <option v-for="role in boardRoles" :key="role" :value="role">{{ role }}</option>
        </select>
      </label>
      <p v-if="inviteError" role="alert" class="text-sm text-red-600">{{ inviteError }}</p>
      <button
        type="submit"
        :disabled="inviting"
        class="press h-11 w-fit rounded-full bg-ink px-6 text-sm font-semibold text-white disabled:opacity-60"
      >
        {{ inviting ? 'Creating…' : 'Create invite link' }}
      </button>
      <div v-if="createdLink" class="pop flex flex-col gap-2 rounded-xl bg-hover p-3">
        <span class="text-xs font-semibold text-muted">Shown once. Copy it now.</span>
        <div class="flex items-center gap-2">
          <input
            :value="createdLink"
            readonly
            aria-label="Invite link"
            class="h-9 min-w-0 flex-1 rounded-[10px] border border-line bg-panel px-2 text-xs"
            @focus="($event.target as HTMLInputElement).select()"
          />
          <button
            type="button"
            class="press h-9 rounded-full bg-accent px-4 text-xs font-semibold text-white"
            @click="copyLink"
          >
            Copy
          </button>
        </div>
      </div>
      <ul v-if="store.invites.length" class="flex flex-col gap-2" aria-label="Pending invites">
        <li
          v-for="invite in store.invites"
          :key="invite.id"
          class="flex items-center gap-3 text-sm"
        >
          <span class="min-w-0 flex-1 truncate">{{ invite.email ?? 'Link invite' }}</span>
          <span class="text-xs text-muted"
            >{{ invite.role }} · until {{ expires(invite.expiresAt) }}</span
          >
          <button
            type="button"
            class="ghost rounded-full px-3 py-1 text-xs font-semibold text-red-700"
            :aria-label="`Revoke invite for ${invite.email ?? 'link'}`"
            @click="revoke(invite.id)"
          >
            Revoke
          </button>
        </li>
      </ul>
    </form>

    <form
      v-if="store.isOwner"
      :class="sectionClass"
      aria-labelledby="settings-template"
      @submit.prevent="saveTemplate"
    >
      <h3 id="settings-template" class="text-base font-semibold">Save as template</h3>
      <p class="-mt-2 text-xs text-muted">
        Keeps the lists, labels and cards (titles, descriptions, checklists). Members, comments,
        files, dates and assignees stay out. Only you see your templates.
      </p>
      <label class="flex flex-col gap-1.5 text-[13px] font-semibold">
        Template name
        <input v-model="templateName" type="text" maxlength="120" required :class="inputClass" />
      </label>
      <label class="flex items-center gap-2 text-sm">
        <input
          v-model="templateCards"
          type="checkbox"
          class="h-4 w-4 accent-[var(--color-accent)]"
        />
        Include cards
      </label>
      <p v-if="templateError" role="alert" class="text-sm text-red-600">{{ templateError }}</p>
      <button
        type="submit"
        :disabled="savingTemplate || !templateName.trim()"
        class="press h-11 w-fit rounded-full bg-ink px-6 text-sm font-semibold text-white disabled:opacity-60"
      >
        {{ savingTemplate ? 'Saving…' : 'Save as template' }}
      </button>
    </form>

    <section :class="sectionClass" aria-labelledby="settings-danger">
      <h3 id="settings-danger" class="text-base font-semibold">
        {{ store.isOwner ? 'Archive or delete' : 'Leave board' }}
      </h3>
      <div v-if="store.isOwner" class="flex flex-wrap gap-2">
        <button
          type="button"
          class="ghost h-10 rounded-full border border-line px-5 text-sm font-semibold"
          @click="archiveOrRestore"
        >
          {{ board.archivedAt ? 'Restore board' : 'Archive board' }}
        </button>
        <button
          v-if="board.archivedAt"
          type="button"
          class="h-10 rounded-full bg-red-700 px-5 text-sm font-semibold text-white"
          @click="deleteBoard"
        >
          {{ confirmDelete ? 'Click again to delete forever' : 'Delete board' }}
        </button>
      </div>
      <p v-if="store.isOwner && !board.archivedAt" class="-mt-2 text-xs text-muted">
        Archived boards can be restored from Home, or deleted for good.
      </p>
      <button
        v-if="!store.isOwner"
        type="button"
        class="ghost h-10 w-fit rounded-full border border-line px-5 text-sm font-semibold text-red-700"
        @click="leave"
      >
        Leave this board
      </button>
    </section>
  </SideSheet>
</template>
