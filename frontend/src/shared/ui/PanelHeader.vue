<script setup lang="ts">
import { RouterLink } from 'vue-router'

import UserAvatar from './UserAvatar.vue'

/**
 * Header row at the top of the white panel: title, an optional chip, actions pushed right,
 * then the signed-in user's avatar linking to Activity and profile.
 */
defineProps<{
  title?: string
  user?: { displayName: string; email: string; avatarColor: string } | null
}>()
</script>

<template>
  <header
    class="flex flex-wrap items-center gap-3 border-b border-[#EEF1F6] px-5 py-[18px] md:px-7"
  >
    <slot name="title">
      <h1 class="text-[22px] font-semibold">{{ title }}</h1>
    </slot>
    <slot name="chip" />
    <div class="ml-auto flex flex-wrap items-center gap-2">
      <slot name="actions" />
      <RouterLink
        v-if="user"
        to="/activity"
        class="press flex rounded-full"
        aria-label="Your profile"
        :title="user.displayName || user.email"
      >
        <UserAvatar
          :name="user.displayName"
          :email="user.email"
          :color="user.avatarColor"
          :size="42"
        />
      </RouterLink>
    </div>
  </header>
</template>
