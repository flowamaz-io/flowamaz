<script setup lang="ts">
import { storeToRefs } from 'pinia';
import { LayoutDashboard, Settings, Users, KeyRound, Workflow, Activity, Plug, X, CloudSun } from 'lucide-vue-next';
import { useUiStore } from '@/stores/ui.store';
import { APP_NAME } from '@/utils/constants';

const ui = useUiStore();
const { sidebarCollapsed } = storeToRefs(ui);

interface NavItem {
  label: string;
  to?: string;
  icon: typeof LayoutDashboard;
  disabled?: boolean;
  badge?: string;
}

const primary: NavItem[] = [
  { label: 'Dashboard', to: '/', icon: LayoutDashboard },
  { label: 'Workflows', to: '/workflows', icon: Workflow },
  { label: 'Weather', to: '/weather', icon: CloudSun },
  { label: 'Instances', to: '/instances', icon: Activity },
  { label: 'Library', icon: Plug, disabled: true, badge: 'Phase 4' },
];

const settings: NavItem[] = [
  { label: 'Workspace', to: '/settings', icon: Settings },
  { label: 'Members', to: '/settings/members', icon: Users },
  { label: 'API keys', to: '/settings/api-keys', icon: KeyRound },
];
</script>

<template>
  <!-- Backdrop on mobile when expanded -->
  <div
    v-if="!sidebarCollapsed"
    class="fixed inset-0 z-30 bg-slate-900/40 md:hidden"
    @click="ui.setSidebar(true)"
  />
  <aside
    :class="[
      'fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r border-slate-200 bg-white transition-transform md:static md:translate-x-0',
      sidebarCollapsed ? '-translate-x-full' : 'translate-x-0',
    ]"
  >
    <div class="flex h-14 items-center justify-between border-b border-slate-200 px-4">
      <div class="flex items-center gap-2">
        <div class="flex h-7 w-7 items-center justify-center rounded-lg bg-primary-600 text-sm font-bold text-white">
          F
        </div>
        <span class="font-semibold text-slate-900">{{ APP_NAME }}</span>
      </div>
      <button
        class="rounded p-1 text-slate-400 hover:bg-slate-100 md:hidden"
        aria-label="Close menu"
        @click="ui.setSidebar(true)"
      >
        <X class="h-5 w-5" />
      </button>
    </div>

    <nav class="flex-1 space-y-6 overflow-y-auto px-3 py-4">
      <div>
        <ul class="space-y-1">
          <li
            v-for="item in primary"
            :key="item.label"
          >
            <RouterLink
              v-if="!item.disabled && item.to"
              :to="item.to"
              class="flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
              active-class="bg-primary-50 text-primary-700"
              exact-active-class="bg-primary-50 text-primary-700"
              @click="ui.setSidebar(true)"
            >
              <component
                :is="item.icon"
                class="h-4 w-4"
              />
              {{ item.label }}
            </RouterLink>
            <span
              v-else
              class="flex cursor-not-allowed items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-slate-300"
            >
              <component
                :is="item.icon"
                class="h-4 w-4"
              />
              {{ item.label }}
              <span class="ml-auto rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-semibold text-slate-400">{{ item.badge }}</span>
            </span>
          </li>
        </ul>
      </div>
      <div>
        <p class="px-3 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">
          Settings
        </p>
        <ul class="space-y-1">
          <li
            v-for="item in settings"
            :key="item.label"
          >
            <RouterLink
              :to="item.to!"
              class="flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
              active-class="bg-primary-50 text-primary-700"
              @click="ui.setSidebar(true)"
            >
              <component
                :is="item.icon"
                class="h-4 w-4"
              />
              {{ item.label }}
            </RouterLink>
          </li>
        </ul>
      </div>
    </nav>
  </aside>
</template>
