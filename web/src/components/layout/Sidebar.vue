<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { storeToRefs } from 'pinia';
import {
  Activity, BarChart2, BookOpen, ChevronLeft, ChevronRight, CircleCheck, CloudSun,
  HelpCircle, HeartPulse, LayoutDashboard, Microscope, Plug, Settings, Sparkles,
  Terminal, Workflow, X, Upload, KeyRound, Users, Webhook, ShieldCheck, ScrollText,
} from 'lucide-vue-next';
import { useUiStore } from '@/stores/ui.store';
import { useWorkspace } from '@/composables/useWorkspace';
import { useHelp } from '@/composables/useHelp';
import { gateService } from '@/services/gate.service';
import { APP_NAME } from '@/utils/constants';
import WorkspaceSwitcher from './WorkspaceSwitcher.vue';
import UserMenu from './UserMenu.vue';
import NotificationBell from './NotificationBell.vue';
import EditionBanner from './EditionBanner.vue';

const ui = useUiStore();
const { sidebarCollapsed, sidebarMinimized } = storeToRefs(ui);
const ws = useWorkspace();
const { current, currentWorkspaceId } = ws;
const help = useHelp();

interface NavItem {
  label: string;
  to?: string;
  href?: string;
  icon: typeof LayoutDashboard;
  badgeKey?: 'gates';
}

interface NavSection {
  label: string;
  items: NavItem[];
  // Designer+ only.
  designerOnly?: boolean;
}

// Process Intel routes to /weather — the Workflow Weather view is the process-intelligence
// surface (no dedicated /process-intel route exists). Connectors Health → /library/health.
const sections: NavSection[] = [
  {
    label: 'Main',
    items: [
      { label: 'Dashboard', to: '/', icon: LayoutDashboard },
      { label: 'Workflows', to: '/workflows', icon: Workflow },
      { label: 'Instances', to: '/instances', icon: Activity },
      { label: 'Gates', to: '/gates', icon: CircleCheck, badgeKey: 'gates' },
      { label: 'Weather', to: '/weather', icon: CloudSun },
    ],
  },
  {
    label: 'Build',
    items: [
      { label: 'Library', to: '/library', icon: Plug },
      { label: 'Connector Health', to: '/library/health', icon: HeartPulse },
      { label: 'Submit Connector', to: '/library/submit', icon: Upload },
    ],
  },
  {
    label: 'Settings',
    items: [
      { label: 'Workspace', to: '/settings', icon: Settings },
      { label: 'Members', to: '/settings/members', icon: Users },
      { label: 'API Keys', to: '/settings/api-keys', icon: KeyRound },
      { label: 'Webhooks', to: '/settings/webhooks', icon: Webhook },
      { label: 'SSO', to: '/settings/sso', icon: ShieldCheck },
      { label: 'Audit Log', to: '/settings/audit', icon: ScrollText },
    ],
  },
  {
    label: 'Insights',
    items: [
      { label: 'Analytics', to: '/analytics', icon: BarChart2 },
      { label: 'Process Intel', to: '/weather', icon: Microscope },
    ],
  },
  {
    label: 'Developer',
    designerOnly: true,
    items: [
      { label: 'CLI Docs', href: 'https://docs.flowamaz.io', icon: Terminal },
      { label: 'API Reference', href: 'https://docs.flowamaz.io', icon: BookOpen },
    ],
  },
];

const isDesigner = computed(() => {
  const role = current.value?.role;
  // Designer+ = Designer or Admin. When role is unknown, show the section.
  return !role || role === 'Designer' || role === 'Admin';
});

const visibleSections = computed(() =>
  sections.filter((s) => !s.designerOnly || isDesigner.value),
);

// ── Gates pending badge ──────────────────────────────────────────────────────
const pendingGates = ref(0);

async function loadPendingGates(): Promise<void> {
  const wsId = currentWorkspaceId.value;
  if (!wsId) {
    pendingGates.value = 0;
    return;
  }
  try {
    const gates = await gateService.getPendingGates(wsId);
    pendingGates.value = gates.length;
  } catch {
    // Degrade silently to no badge — the badge is non-essential.
    pendingGates.value = 0;
  }
}

function badgeCount(item: NavItem): number {
  return item.badgeKey === 'gates' ? pendingGates.value : 0;
}

onMounted(loadPendingGates);
watch(currentWorkspaceId, loadPendingGates);

function openHelp(): void {
  help.openForRoute(window.location.pathname);
}

function closeMobile(): void {
  ui.setSidebar(true);
}
</script>

<template>
  <!-- Backdrop on mobile when expanded -->
  <div
    v-if="!sidebarCollapsed"
    class="fixed inset-0 z-30 bg-slate-900/60 md:hidden"
    @click="closeMobile"
  />
  <aside
    :class="[
      'fixed inset-y-0 left-0 z-40 flex flex-col bg-sidebar-bg border-r border-sidebar-border',
      'transition-all duration-200 ease-in-out',
      'md:static md:translate-x-0',
      sidebarCollapsed ? '-translate-x-full' : 'translate-x-0',
      sidebarMinimized ? 'w-[220px] md:w-[52px]' : 'w-[220px]',
    ]"
  >
    <!-- Header: logo + collapse toggle -->
    <div
      :class="[
        'flex h-14 items-center border-b border-sidebar-border px-3',
        sidebarMinimized ? 'md:justify-center' : 'justify-between',
      ]"
    >
      <div class="flex items-center gap-2">
        <Sparkles class="h-6 w-6 shrink-0 text-sidebar-active" />
        <span :class="['font-semibold text-sidebar-text-primary', { 'md:hidden': sidebarMinimized }]">{{ APP_NAME }}</span>
      </div>
      <button
        class="rounded p-1 text-sidebar-text-secondary hover:bg-sidebar-hover md:hidden"
        aria-label="Close menu"
        @click="closeMobile"
      >
        <X class="h-5 w-5" />
      </button>
      <button
        :class="[
          'hidden rounded p-1 text-sidebar-text-secondary hover:bg-sidebar-hover md:block',
          sidebarMinimized ? 'md:hidden' : '',
        ]"
        aria-label="Collapse sidebar"
        @click="ui.toggleSidebarMinimized()"
      >
        <ChevronLeft class="h-5 w-5" />
      </button>
    </div>

    <!-- Workspace switcher -->
    <div class="border-b border-sidebar-border px-2 py-2">
      <WorkspaceSwitcher :collapsed="sidebarMinimized" />
    </div>
    <EditionBanner :collapsed="sidebarMinimized" />

    <!-- Nav -->
    <nav class="flex-1 space-y-4 overflow-y-auto px-2 py-3">
      <div
        v-for="section in visibleSections"
        :key="section.label"
      >
        <p
          v-if="!sidebarMinimized"
          class="px-3 py-2 text-xs uppercase tracking-wider text-sidebar-text-muted"
        >
          {{ section.label }}
        </p>
        <ul class="space-y-1">
          <li
            v-for="item in section.items"
            :key="item.label"
            class="group relative"
          >
            <!-- External link (Developer docs) -->
            <a
              v-if="item.href"
              :href="item.href"
              target="_blank"
              rel="noopener noreferrer"
              :class="[
                'flex items-center rounded-lg py-2 text-sm font-medium text-sidebar-text-secondary hover:bg-sidebar-hover hover:text-sidebar-text-primary',
                sidebarMinimized ? 'md:justify-center md:px-2 gap-3 px-3' : 'gap-3 px-3',
              ]"
            >
              <component
                :is="item.icon"
                class="h-4 w-4 shrink-0"
              />
              <span :class="{ 'md:hidden': sidebarMinimized }">{{ item.label }}</span>
            </a>
            <!-- Internal route -->
            <RouterLink
              v-else-if="item.to"
              :to="item.to"
              :class="[
                'flex items-center rounded-lg border-l-2 border-transparent py-2 text-sm font-medium text-sidebar-text-secondary hover:bg-sidebar-hover hover:text-sidebar-text-primary',
                sidebarMinimized ? 'md:justify-center md:px-2 gap-3 px-3' : 'gap-3 px-3',
              ]"
              active-class="border-sidebar-active bg-sidebar-hover text-sidebar-text-primary [&_svg]:text-sidebar-active"
              :exact-active-class="item.to === '/' ? 'border-sidebar-active bg-sidebar-hover text-sidebar-text-primary [&_svg]:text-sidebar-active' : ''"
              @click="closeMobile"
            >
              <component
                :is="item.icon"
                class="h-4 w-4 shrink-0"
              />
              <span :class="['flex-1', { 'md:hidden': sidebarMinimized }]">{{ item.label }}</span>
              <span
                v-if="badgeCount(item) > 0 && !sidebarMinimized"
                class="ml-auto rounded-full bg-amber-400 px-1.5 py-0.5 text-[10px] font-semibold text-white"
              >{{ badgeCount(item) }}</span>
            </RouterLink>

            <!-- Tooltip (desktop collapsed only) -->
            <span
              v-if="sidebarMinimized"
              class="pointer-events-none absolute left-full top-1/2 z-50 ml-2 hidden -translate-y-1/2 whitespace-nowrap rounded bg-slate-900 px-2 py-1 text-xs text-white opacity-0 transition-opacity group-hover:opacity-100 md:block"
            >{{ item.label }}<template v-if="badgeCount(item) > 0"> ({{ badgeCount(item) }})</template></span>
          </li>
        </ul>
      </div>
    </nav>

    <!-- Bottom: Notifications, Settings, Help, expand toggle, user -->
    <div class="border-t border-sidebar-border px-2 py-2 space-y-1">
      <NotificationBell :collapsed="sidebarMinimized" />
      <div class="group relative">
        <RouterLink
          to="/settings"
          :class="[
            'flex items-center rounded-lg border-l-2 border-transparent py-2 text-sm font-medium text-sidebar-text-secondary hover:bg-sidebar-hover hover:text-sidebar-text-primary',
            sidebarMinimized ? 'md:justify-center md:px-2 gap-3 px-3' : 'gap-3 px-3',
          ]"
          active-class="border-sidebar-active bg-sidebar-hover text-sidebar-text-primary [&_svg]:text-sidebar-active"
          @click="closeMobile"
        >
          <Settings class="h-4 w-4 shrink-0" />
          <span :class="{ 'md:hidden': sidebarMinimized }">Settings</span>
        </RouterLink>
        <span
          v-if="sidebarMinimized"
          class="pointer-events-none absolute left-full top-1/2 z-50 ml-2 hidden -translate-y-1/2 whitespace-nowrap rounded bg-slate-900 px-2 py-1 text-xs text-white opacity-0 transition-opacity group-hover:opacity-100 md:block"
        >Settings</span>
      </div>
      <div class="group relative">
        <button
          type="button"
          :class="[
            'flex w-full items-center rounded-lg py-2 text-sm font-medium text-sidebar-text-secondary hover:bg-sidebar-hover hover:text-sidebar-text-primary',
            sidebarMinimized ? 'md:justify-center md:px-2 gap-3 px-3' : 'gap-3 px-3',
          ]"
          aria-label="Open help"
          @click="openHelp"
        >
          <HelpCircle class="h-4 w-4 shrink-0" />
          <span :class="{ 'md:hidden': sidebarMinimized }">Help</span>
        </button>
        <span
          v-if="sidebarMinimized"
          class="pointer-events-none absolute left-full top-1/2 z-50 ml-2 hidden -translate-y-1/2 whitespace-nowrap rounded bg-slate-900 px-2 py-1 text-xs text-white opacity-0 transition-opacity group-hover:opacity-100 md:block"
        >Help</span>
      </div>

      <!-- Expand toggle when collapsed (desktop only) -->
      <button
        v-if="sidebarMinimized"
        type="button"
        class="hidden w-full items-center justify-center rounded-lg py-2 text-sidebar-text-secondary hover:bg-sidebar-hover md:flex"
        aria-label="Expand sidebar"
        @click="ui.toggleSidebarMinimized()"
      >
        <ChevronRight class="h-4 w-4" />
      </button>

      <UserMenu :collapsed="sidebarMinimized" />
    </div>
  </aside>
</template>
