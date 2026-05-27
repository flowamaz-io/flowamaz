<script setup lang="ts">
import { storeToRefs } from 'pinia';
import {
  LayoutDashboard, Settings, Users, KeyRound, Workflow, Activity, Plug, X, CloudSun,
  ChevronLeft, ChevronRight,
} from 'lucide-vue-next';
import { useUiStore } from '@/stores/ui.store';
import { APP_NAME } from '@/utils/constants';

const ui = useUiStore();
const { sidebarCollapsed, sidebarMinimized } = storeToRefs(ui);

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
  { label: 'Library', to: '/library', icon: Plug },
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
      'fixed inset-y-0 left-0 z-40 flex flex-col border-r border-slate-200 bg-white',
      'transition-all duration-200 ease-in-out',
      'md:static md:translate-x-0',
      sidebarCollapsed ? '-translate-x-full' : 'translate-x-0',
      sidebarMinimized ? 'w-64 md:w-14' : 'w-64 md:w-48',
    ]"
  >
    <!-- Header -->
    <div
      :class="[
        'flex h-14 items-center border-b border-slate-200 px-4',
        sidebarMinimized ? 'md:justify-center' : 'justify-between',
      ]"
    >
      <div class="flex items-center gap-2">
        <div class="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-primary-600 text-sm font-bold text-white">
          F
        </div>
        <span
          :class="['font-semibold text-slate-900 truncate', { 'md:hidden': sidebarMinimized }]"
        >{{ APP_NAME }}</span>
      </div>
      <button
        class="rounded p-1 text-slate-400 hover:bg-slate-100 md:hidden"
        aria-label="Close menu"
        @click="ui.setSidebar(true)"
      >
        <X class="h-5 w-5" />
      </button>
    </div>

    <!-- Nav -->
    <nav class="flex-1 space-y-6 overflow-y-auto px-2 py-4">
      <div>
        <ul class="space-y-1">
          <li
            v-for="item in primary"
            :key="item.label"
            class="relative group"
          >
            <RouterLink
              v-if="!item.disabled && item.to"
              :to="item.to"
              :class="[
                'flex items-center rounded-lg py-2 text-sm font-medium text-slate-600 hover:bg-slate-100',
                sidebarMinimized ? 'md:justify-center md:px-2 gap-3 px-3' : 'gap-3 px-3',
              ]"
              active-class="bg-primary-50 text-primary-700"
              exact-active-class="bg-primary-50 text-primary-700"
              @click="ui.setSidebar(true)"
            >
              <component :is="item.icon" class="h-4 w-4 shrink-0" />
              <span :class="{ 'md:hidden': sidebarMinimized }">{{ item.label }}</span>
            </RouterLink>
            <span
              v-else
              :class="[
                'flex cursor-not-allowed items-center rounded-lg py-2 text-sm font-medium text-slate-300',
                sidebarMinimized ? 'md:justify-center md:px-2 gap-3 px-3' : 'gap-3 px-3',
              ]"
            >
              <component :is="item.icon" class="h-4 w-4 shrink-0" />
              <span :class="{ 'md:hidden': sidebarMinimized }">{{ item.label }}</span>
              <span
                v-if="item.badge && !sidebarMinimized"
                class="ml-auto rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-semibold text-slate-400"
              >{{ item.badge }}</span>
            </span>
            <!-- Tooltip (desktop collapsed only) -->
            <span
              v-if="sidebarMinimized"
              class="pointer-events-none absolute left-full top-1/2 z-50 ml-2 -translate-y-1/2 whitespace-nowrap rounded bg-gray-900 px-2 py-1 text-xs text-white opacity-0 transition-opacity group-hover:opacity-100 hidden md:block"
            >{{ item.label }}</span>
          </li>
        </ul>
      </div>

      <div>
        <p
          v-if="!sidebarMinimized"
          class="px-3 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400 md:block"
        >
          Settings
        </p>
        <p
          v-else
          class="hidden md:block px-3 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400"
        />
        <ul class="space-y-1">
          <li
            v-for="item in settings"
            :key="item.label"
            class="relative group"
          >
            <RouterLink
              :to="item.to!"
              :class="[
                'flex items-center rounded-lg py-2 text-sm font-medium text-slate-600 hover:bg-slate-100',
                sidebarMinimized ? 'md:justify-center md:px-2 gap-3 px-3' : 'gap-3 px-3',
              ]"
              active-class="bg-primary-50 text-primary-700"
              @click="ui.setSidebar(true)"
            >
              <component :is="item.icon" class="h-4 w-4 shrink-0" />
              <span :class="{ 'md:hidden': sidebarMinimized }">{{ item.label }}</span>
            </RouterLink>
            <!-- Tooltip (desktop collapsed only) -->
            <span
              v-if="sidebarMinimized"
              class="pointer-events-none absolute left-full top-1/2 z-50 ml-2 -translate-y-1/2 whitespace-nowrap rounded bg-gray-900 px-2 py-1 text-xs text-white opacity-0 transition-opacity group-hover:opacity-100 hidden md:block"
            >{{ item.label }}</span>
          </li>
        </ul>
      </div>
    </nav>

    <!-- Collapse toggle — desktop only -->
    <div class="hidden md:flex items-center border-t border-slate-200 px-2 py-2">
      <button
        :class="[
          'w-full flex items-center h-10 rounded-lg cursor-pointer transition-colors hover:bg-slate-100',
          sidebarMinimized ? 'justify-center' : 'justify-end px-2',
        ]"
        :aria-label="sidebarMinimized ? 'Expand sidebar' : 'Collapse sidebar'"
        @click="ui.toggleSidebarMinimized()"
      >
        <ChevronRight v-if="sidebarMinimized" class="h-4 w-4 text-slate-400" />
        <ChevronLeft v-else class="h-4 w-4 text-slate-400" />
      </button>
    </div>
  </aside>
</template>
