<script setup lang="ts">
import { computed, nextTick, ref } from 'vue';
import { onClickOutside } from '@vueuse/core';
import { Building2, ChevronDown, Plus, Search } from 'lucide-vue-next';
import { useWorkspace } from '@/composables/useWorkspace';
import { useAuth } from '@/composables/useAuth';
import { avatarColor, initial } from '@/utils/string.util';
import FmBadge from '@/components/common/FmBadge.vue';
import CreateWorkspaceModal from '@/components/workspace/CreateWorkspaceModal.vue';

// Collapsed (icon-only) hides the labels and renders just the workspace initial.
defineProps<{ collapsed?: boolean }>();

const ws = useWorkspace();
const { allWorkspaces, current } = ws;
const auth = useAuth();
const { user } = auth;

const createOpen = ref(false);

const open = ref(false);
const query = ref('');
const highlighted = ref(0);
const root = ref<HTMLElement | null>(null);
const searchInput = ref<HTMLInputElement | null>(null);

onClickOutside(root, () => close());

const filtered = computed(() => {
  const q = query.value.trim().toLowerCase();
  if (!q) return allWorkspaces.value;
  return allWorkspaces.value.filter((w) => w.name.toLowerCase().includes(q));
});

async function toggle(): Promise<void> {
  open.value = !open.value;
  if (open.value) {
    query.value = '';
    highlighted.value = Math.max(
      0,
      filtered.value.findIndex((w) => w.id === current.value?.id),
    );
    await nextTick();
    searchInput.value?.focus();
  }
}

function close(): void {
  open.value = false;
}

function select(id: string): void {
  ws.switchWorkspace(id);
  close();
}

function openCreate(): void {
  close();
  createOpen.value = true;
}

function onSearchKeydown(event: KeyboardEvent): void {
  if (event.key === 'ArrowDown') {
    event.preventDefault();
    highlighted.value = Math.min(highlighted.value + 1, filtered.value.length - 1);
  } else if (event.key === 'ArrowUp') {
    event.preventDefault();
    highlighted.value = Math.max(highlighted.value - 1, 0);
  } else if (event.key === 'Enter') {
    event.preventDefault();
    const target = filtered.value[highlighted.value];
    if (target) select(target.id);
  } else if (event.key === 'Escape') {
    event.preventDefault();
    close();
  }
}
</script>

<template>
  <div
    ref="root"
    class="relative"
  >
    <button
      type="button"
      :class="[
        'flex w-full items-center rounded-lg px-2 py-2 text-left transition-colors hover:bg-sidebar-hover',
        collapsed ? 'justify-center' : 'gap-2',
      ]"
      :aria-label="current ? `Workspace: ${current.name}` : 'Select workspace'"
      :title="collapsed && current ? current.name : undefined"
      @click="toggle"
    >
      <span class="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-primary-600 text-sm font-semibold text-white">
        {{ current ? initial(current.name) : 'W' }}
      </span>
      <span
        v-if="!collapsed"
        class="min-w-0 flex-1"
      >
        <span class="flex items-center gap-1 text-[11px] text-sidebar-text-muted">
          <Building2 class="h-3 w-3 shrink-0" />
          <span class="truncate">{{ user?.orgSlug ?? 'Organisation' }}</span>
        </span>
        <span class="block truncate text-sm font-medium text-sidebar-text-primary">
          {{ current?.name ?? 'Select workspace' }}
        </span>
      </span>
      <ChevronDown
        v-if="!collapsed"
        class="h-4 w-4 shrink-0 text-sidebar-text-secondary"
      />
    </button>

    <div
      v-if="open"
      class="absolute left-0 top-full z-50 mt-1 w-64 overflow-hidden rounded-lg border border-sidebar-border bg-sidebar-bg shadow-xl"
      role="menu"
    >
      <div class="flex items-center gap-2 border-b border-sidebar-border px-3 py-2">
        <Search class="h-4 w-4 shrink-0 text-sidebar-text-secondary" />
        <input
          ref="searchInput"
          v-model="query"
          type="text"
          placeholder="Search workspaces…"
          class="w-full bg-transparent text-sm text-sidebar-text-primary placeholder:text-sidebar-text-muted focus:outline-none"
          @keydown="onSearchKeydown"
        >
      </div>
      <ul class="max-h-64 overflow-y-auto py-1">
        <li
          v-for="(w, idx) in filtered"
          :key="w.id"
        >
          <button
            type="button"
            :class="[
              'flex w-full items-center gap-2 px-3 py-2 text-left text-sm transition-colors',
              idx === highlighted ? 'bg-sidebar-hover' : 'hover:bg-sidebar-hover',
            ]"
            @click="select(w.id)"
            @mouseenter="highlighted = idx"
          >
            <span
              :class="[
                'flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-xs font-semibold text-white',
                avatarColor(w.name),
              ]"
            >
              {{ initial(w.name) }}
            </span>
            <span class="min-w-0 flex-1">
              <span
                :class="[
                  'block truncate font-medium',
                  current?.id === w.id ? 'text-sidebar-active' : 'text-sidebar-text-primary',
                ]"
              >{{ w.name }}</span>
              <span class="block truncate text-[11px] text-sidebar-text-muted">{{ w.slug }}</span>
            </span>
            <FmBadge
              variant="slate"
              class="shrink-0"
            >
              {{ w.role }}
            </FmBadge>
            <span
              :class="[
                'h-2 w-2 shrink-0 rounded-full',
                current?.id === w.id ? 'bg-sidebar-active' : 'bg-transparent',
              ]"
            />
          </button>
        </li>
        <li
          v-if="filtered.length === 0"
          class="px-3 py-3 text-sm text-sidebar-text-muted"
        >
          No workspaces match "{{ query }}". Try a different name or create one below.
        </li>
      </ul>
      <button
        type="button"
        class="flex w-full items-center gap-2 border-t border-sidebar-border px-3 py-2 text-left text-sm font-medium text-sidebar-active transition-colors hover:bg-sidebar-hover"
        @click="openCreate"
      >
        <Plus class="h-4 w-4 shrink-0" />
        Create workspace
      </button>
    </div>

    <CreateWorkspaceModal
      :open="createOpen"
      @close="createOpen = false"
      @created="createOpen = false"
    />
  </div>
</template>
