<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { Bell } from 'lucide-vue-next';
import NotificationDropdown from './NotificationDropdown.vue';
import { notificationService, type AppNotification } from '@/services/notification.service';

withDefaults(defineProps<{ collapsed?: boolean }>(), { collapsed: false });

const router = useRouter();
const open = ref(false);
const notifications = ref<AppNotification[]>([]);
const unread = ref(0);
let timer: ReturnType<typeof setInterval> | undefined;

const badge = computed(() => (unread.value > 9 ? '9+' : String(unread.value)));

async function refresh(): Promise<void> {
  try {
    [notifications.value, unread.value] = await Promise.all([
      notificationService.list(false, 20),
      notificationService.unreadCount(),
    ]);
  } catch {
    // Non-essential — degrade silently if notifications can't be fetched.
  }
}

async function toggle(): Promise<void> {
  open.value = !open.value;
  if (open.value) await refresh();
}

async function markRead(id: string): Promise<void> {
  await notificationService.markRead(id);
  await refresh();
}

async function markAllRead(): Promise<void> {
  await notificationService.markAllRead();
  await refresh();
}

function navigate(n: AppNotification): void {
  open.value = false;
  if (!n.isRead) void notificationService.markRead(n.id).then(refresh);
  if (n.actionUrl) void router.push(n.actionUrl);
}

function viewAll(): void {
  open.value = false;
  void router.push('/instances');
}

onMounted(() => {
  void refresh();
  // Auto-poll every 60s (not WebSocket — keep it simple).
  timer = setInterval(refresh, 60_000);
});
onUnmounted(() => {
  if (timer) clearInterval(timer);
});
</script>

<template>
  <div class="relative">
    <button
      type="button"
      :class="[
        'flex w-full items-center rounded-lg py-2 text-sm font-medium text-sidebar-text-secondary hover:bg-sidebar-hover hover:text-sidebar-text-primary',
        collapsed ? 'md:justify-center md:px-2 gap-3 px-3' : 'gap-3 px-3',
      ]"
      aria-label="Open notifications"
      @click="toggle"
    >
      <span class="relative shrink-0">
        <Bell class="h-4 w-4" />
        <span
          v-if="unread > 0"
          class="absolute -right-1.5 -top-1.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-semibold text-white"
          data-test="notification-badge"
        >{{ badge }}</span>
      </span>
      <span :class="{ 'md:hidden': collapsed }">Notifications</span>
    </button>

    <div
      v-if="open"
      class="absolute bottom-full left-0 z-50 mb-2"
    >
      <NotificationDropdown
        :notifications="notifications"
        @mark-read="markRead"
        @mark-all-read="markAllRead"
        @navigate="navigate"
        @view-all="viewAll"
      />
    </div>
  </div>
</template>
