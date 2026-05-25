<script setup lang="ts">
import { computed } from 'vue';
import { CheckCircle2, AlertCircle, AlertTriangle, Info, X } from 'lucide-vue-next';
import { storeToRefs } from 'pinia';
import { useUiStore } from '@/stores/ui.store';
import type { Toast } from '@/types';

const ui = useUiStore();
const { toasts } = storeToRefs(ui);

const iconFor = computed(() => (t: Toast['type']) =>
  ({ success: CheckCircle2, error: AlertCircle, warning: AlertTriangle, info: Info })[t],
);

const colorFor = (t: Toast['type']): string =>
  ({
    success: 'text-primary-600',
    error: 'text-danger-600',
    warning: 'text-amber-600',
    info: 'text-blue-600',
  })[t];
</script>

<template>
  <div class="pointer-events-none fixed inset-x-0 bottom-0 z-[70] flex flex-col items-center gap-2 p-4 sm:items-end">
    <TransitionGroup
      enter-active-class="transition duration-200 ease-out"
      enter-from-class="translate-y-2 opacity-0"
      leave-active-class="transition duration-150 ease-in"
      leave-to-class="opacity-0"
    >
      <div
        v-for="toast in toasts"
        :key="toast.id"
        class="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-lg border border-slate-200 bg-white px-4 py-3 shadow-lg"
        role="status"
      >
        <component
          :is="iconFor(toast.type)"
          :class="['mt-0.5 h-5 w-5 shrink-0', colorFor(toast.type)]"
        />
        <p class="flex-1 text-sm text-slate-700">
          {{ toast.message }}
        </p>
        <button
          type="button"
          class="shrink-0 rounded p-0.5 text-slate-400 hover:bg-slate-100"
          aria-label="Dismiss"
          @click="ui.dismissToast(toast.id)"
        >
          <X class="h-4 w-4" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>
