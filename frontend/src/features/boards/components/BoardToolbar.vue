<script setup lang="ts">
const views = [
  { label: 'Board', icon: 'M3 4h18v16H3zM9 4v16M15 4v16', active: true },
  { label: 'List', icon: 'M9 6h11M9 12h11M9 18h11M4 6h.01M4 12h.01M4 18h.01', active: false },
  { label: 'Calendar', icon: 'M3 5h18v16H3zM16 3v4M8 3v4M3 10h18', active: false },
]

const tools = [
  { label: 'Filter', icon: 'M3 5h18l-7 8v6l-4 2v-8z' },
  { label: 'Sort', icon: 'M7 4v16M3 16l4 4 4-4M17 20V4M13 8l4-4 4 4' },
  { label: 'Group by', icon: 'M3 3h7v7H3zM14 3h7v7h-7zM3 14h18v7H3z' },
]

const later = 'Coming in 1.1'
</script>

<template>
  <div class="flex flex-wrap items-center gap-3 border-b border-[#EEF1F6] px-5 py-3.5 md:px-7">
    <div role="tablist" aria-label="Board view" class="flex gap-1 rounded-[14px] bg-hover p-1">
      <button
        v-for="view in views"
        :key="view.label"
        type="button"
        role="tab"
        :aria-selected="view.active"
        :aria-disabled="!view.active"
        :title="view.active ? undefined : later"
        class="flex h-[38px] items-center gap-2 rounded-[10px] px-4 text-[15px]"
        :class="
          view.active
            ? 'bg-panel font-semibold text-ink shadow-[0_1px_2px_rgb(17_24_39/0.08)]'
            : 'cursor-not-allowed font-medium text-[#374151]'
        "
      >
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="view.icon" />
        </svg>
        {{ view.label }}
      </button>
    </div>
    <div class="ml-auto flex flex-wrap items-center gap-1">
      <button
        v-for="tool in tools"
        :key="tool.label"
        type="button"
        aria-disabled="true"
        :title="later"
        :aria-description="later"
        class="flex h-[38px] cursor-not-allowed items-center gap-1.5 rounded-[10px] px-3 text-sm font-medium text-[#374151] opacity-70"
      >
        <svg
          width="15"
          height="15"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path :d="tool.icon" />
        </svg>
        {{ tool.label }}
      </button>
    </div>
  </div>
</template>
