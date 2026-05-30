<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import { onClickOutside } from '@vueuse/core';
import { ChevronUp, CreditCard, LayoutGrid, LogOut, User } from 'lucide-vue-next';
import { useAuth } from '@/composables/useAuth';
import { useWorkspaceStore } from '@/stores/workspace.store';
import { initial } from '@/utils/string.util';

// Collapsed (icon-only) renders just the avatar; the dropdown still opens.
defineProps<{ collapsed?: boolean }>();

const router = useRouter();
const auth = useAuth();
const { user } = auth;

// Plan badge reflects the live workspace edition (EDITION env var via the API), not a static label.
const workspaceStore = useWorkspaceStore();
const PLAN_LABELS: Record<string, string> = {
  community: 'Community',
  starter: 'Starter Plan',
  pro: 'Pro Plan',
  enterprise: 'Enterprise',
};
const planLabel = computed(
  () => PLAN_LABELS[workspaceStore.currentWorkspace?.edition ?? 'community'] ?? 'Community',
);

const open = ref(false);
const root = ref<HTMLElement | null>(null);

onClickOutside(root, () => (open.value = false));

function toggle(): void {
  open.value = !open.value;
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') open.value = false;
}

function goProfile(): void {
  open.value = false;
  router.push('/settings');
}

function goBilling(): void {
  open.value = false;
  router.push('/settings');
}

function goWorkspaces(): void {
  open.value = false;
  router.push('/workspaces');
}

async function logout(): Promise<void> {
  open.value = false;
  await auth.logout();
  router.push('/login');
}
</script>

<template>
  <div
    ref="root"
    class="relative"
    @keydown="onKeydown"
  >
    <button
      type="button"
      :class="[
        'flex w-full items-center rounded-lg px-2 py-2 text-left transition-colors hover:bg-sidebar-hover',
        collapsed ? 'justify-center' : 'gap-2',
      ]"
      :aria-label="user ? `Account: ${user.name}` : 'Account'"
      :title="collapsed && user ? user.name : undefined"
      @click="toggle"
    >
      <span class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent-600 text-sm font-semibold text-white">
        {{ user ? initial(user.name) : '?' }}
      </span>
      <span
        v-if="!collapsed"
        class="min-w-0 flex-1"
      >
        <span class="block truncate text-sm font-medium text-sidebar-text-primary">
          {{ user?.name ?? 'Account' }}
        </span>
        <span class="inline-flex items-center rounded-full bg-sidebar-hover px-1.5 py-0.5 text-[10px] font-medium text-sidebar-text-secondary">
          {{ planLabel }}
        </span>
      </span>
      <ChevronUp
        v-if="!collapsed"
        class="h-4 w-4 shrink-0 text-sidebar-text-secondary"
      />
    </button>

    <div
      v-if="open"
      class="absolute bottom-full left-0 z-50 mb-1 w-full min-w-[12rem] overflow-hidden rounded-lg border border-sidebar-border bg-sidebar-bg py-1 shadow-xl"
      role="menu"
    >
      <div class="border-b border-sidebar-border px-3 py-2">
        <p class="truncate text-sm font-medium text-sidebar-text-primary">
          {{ user?.name }}
        </p>
        <p class="truncate text-xs text-sidebar-text-muted">
          {{ user?.email }}
        </p>
      </div>
      <button
        type="button"
        class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-sidebar-text-secondary transition-colors hover:bg-sidebar-hover hover:text-sidebar-text-primary"
        @click="goProfile"
      >
        <User class="h-4 w-4" />
        Profile
      </button>
      <button
        type="button"
        class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-sidebar-text-secondary transition-colors hover:bg-sidebar-hover hover:text-sidebar-text-primary"
        @click="goWorkspaces"
      >
        <LayoutGrid class="h-4 w-4" />
        Workspaces
      </button>
      <button
        type="button"
        class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-sidebar-text-secondary transition-colors hover:bg-sidebar-hover hover:text-sidebar-text-primary"
        @click="goBilling"
      >
        <CreditCard class="h-4 w-4" />
        Billing
      </button>
      <button
        type="button"
        class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-sidebar-text-secondary transition-colors hover:bg-sidebar-hover hover:text-sidebar-text-primary"
        @click="logout"
      >
        <LogOut class="h-4 w-4" />
        Logout
      </button>
    </div>
  </div>
</template>
