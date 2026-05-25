<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router';
import { Menu, HelpCircle, ChevronDown, LogOut, Check } from 'lucide-vue-next';
import FmDropdown from '@/components/common/FmDropdown.vue';
import { useUiStore } from '@/stores/ui.store';
import { useAuth } from '@/composables/useAuth';
import { useWorkspace } from '@/composables/useWorkspace';
import { useHelp } from '@/composables/useHelp';
import { initial } from '@/utils/string.util';

const ui = useUiStore();
const route = useRoute();
const router = useRouter();
const auth = useAuth();
const { user } = auth;
const help = useHelp();

const ws = useWorkspace();
const allWorkspaces = ws.allWorkspaces;
const current = ws.current;

function openHelp(): void {
  help.openForRoute(route.path);
}

function selectWorkspace(id: string): void {
  ws.switchWorkspace(id);
}

async function logout(): Promise<void> {
  await auth.logout();
  router.push('/login');
}
</script>

<template>
  <header class="flex h-14 items-center justify-between border-b border-slate-200 bg-white px-4">
    <div class="flex items-center gap-3">
      <button
        class="rounded-lg p-1.5 text-slate-500 hover:bg-slate-100 md:hidden"
        aria-label="Toggle menu"
        @click="ui.toggleSidebar()"
      >
        <Menu class="h-5 w-5" />
      </button>

      <!-- Workspace selector -->
      <FmDropdown position="bottom-left">
        <template #trigger>
          <button class="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50">
            <span class="flex h-5 w-5 items-center justify-center rounded bg-accent-100 text-xs font-semibold text-accent-700">
              {{ current ? initial(current.name) : 'W' }}
            </span>
            <span class="max-w-[10rem] truncate">{{ current?.name ?? 'Select workspace' }}</span>
            <ChevronDown class="h-4 w-4 text-slate-400" />
          </button>
        </template>
        <template #items>
          <button
            v-for="w in allWorkspaces"
            :key="w.id"
            type="button"
            class="flex w-full items-center justify-between px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50"
            @click="selectWorkspace(w.id)"
          >
            <span class="truncate">{{ w.name }}</span>
            <Check
              v-if="current?.id === w.id"
              class="h-4 w-4 text-primary-600"
            />
          </button>
          <p
            v-if="allWorkspaces.length === 0"
            class="px-3 py-2 text-sm text-slate-400"
          >
            No workspaces yet
          </p>
        </template>
      </FmDropdown>
    </div>

    <div class="flex items-center gap-1">
      <button
        class="rounded-lg p-2 text-slate-500 hover:bg-slate-100"
        aria-label="Open help (Shift+?)"
        title="Help (Shift+?)"
        @click="openHelp"
      >
        <HelpCircle class="h-5 w-5" />
      </button>

      <FmDropdown position="bottom-right">
        <template #trigger>
          <button class="flex items-center gap-2 rounded-lg px-2 py-1.5 hover:bg-slate-100">
            <span class="flex h-7 w-7 items-center justify-center rounded-full bg-primary-600 text-sm font-semibold text-white">
              {{ user ? initial(user.name) : '?' }}
            </span>
            <ChevronDown class="hidden h-4 w-4 text-slate-400 sm:block" />
          </button>
        </template>
        <template #items>
          <div class="border-b border-slate-100 px-3 py-2">
            <p class="text-sm font-medium text-slate-800">
              {{ user?.name }}
            </p>
            <p class="truncate text-xs text-slate-400">
              {{ user?.email }}
            </p>
          </div>
          <button
            type="button"
            class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50"
            @click="logout"
          >
            <LogOut class="h-4 w-4" />
            Sign out
          </button>
        </template>
      </FmDropdown>
    </div>
  </header>
</template>
