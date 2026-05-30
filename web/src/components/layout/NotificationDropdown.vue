<script setup lang="ts">
import { CheckCheck } from 'lucide-vue-next';
import { fromNow } from '@/utils/date.util';
import type { AppNotification } from '@/services/notification.service';

defineProps<{ notifications: AppNotification[] }>();
const emit = defineEmits<{
  'mark-read': [id: string];
  'mark-all-read': [];
  navigate: [notification: AppNotification];
  'view-all': [];
}>();
</script>

<template>
  <div class="flex max-h-[480px] w-80 flex-col overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg">
    <div class="flex items-center justify-between border-b border-slate-100 px-4 py-2.5">
      <span class="text-sm font-semibold text-slate-900">Notifications</span>
      <button
        type="button"
        class="flex items-center gap-1 text-xs font-medium text-primary-600 hover:text-primary-700"
        @click="emit('mark-all-read')"
      >
        <CheckCheck class="h-3.5 w-3.5" />
        Mark all read
      </button>
    </div>

    <div
      v-if="notifications.length === 0"
      class="px-4 py-10 text-center text-sm text-slate-400"
    >
      You're all caught up
    </div>

    <ul
      v-else
      class="flex-1 divide-y divide-slate-100 overflow-y-auto"
    >
      <li
        v-for="n in notifications"
        :key="n.id"
        :class="['cursor-pointer px-4 py-3 hover:bg-slate-50', n.isRead ? '' : 'bg-primary-50/40']"
        @click="emit('navigate', n)"
      >
        <div class="flex items-start justify-between gap-2">
          <div class="min-w-0">
            <p class="truncate text-sm font-medium text-slate-800">
              {{ n.title }}
            </p>
            <p class="mt-0.5 text-xs text-slate-500">
              {{ n.message }}
            </p>
            <p class="mt-1 text-[11px] text-slate-400">
              {{ fromNow(n.createdAt) }}
            </p>
          </div>
          <button
            v-if="!n.isRead"
            type="button"
            class="shrink-0 text-[11px] font-medium text-primary-600 hover:text-primary-700"
            @click.stop="emit('mark-read', n.id)"
          >
            Mark read
          </button>
        </div>
      </li>
    </ul>

    <button
      type="button"
      class="border-t border-slate-100 py-2 text-center text-xs font-medium text-primary-600 hover:bg-slate-50"
      @click="emit('view-all')"
    >
      View all
    </button>
  </div>
</template>
