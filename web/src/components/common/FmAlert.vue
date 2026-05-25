<script setup lang="ts">
import { computed, ref } from 'vue';
import { CheckCircle2, AlertCircle, AlertTriangle, Info, X } from 'lucide-vue-next';

const props = withDefaults(
  defineProps<{
    type?: 'success' | 'error' | 'warning' | 'info';
    dismissible?: boolean;
  }>(),
  { type: 'info', dismissible: false },
);

const visible = ref(true);

const config = computed(
  () =>
    ({
      success: { icon: CheckCircle2, wrap: 'bg-primary-50 border-primary-200 text-primary-800', iconColor: 'text-primary-600' },
      error: { icon: AlertCircle, wrap: 'bg-danger-50 border-danger-200 text-danger-800', iconColor: 'text-danger-600' },
      warning: { icon: AlertTriangle, wrap: 'bg-amber-50 border-amber-200 text-amber-800', iconColor: 'text-amber-600' },
      info: { icon: Info, wrap: 'bg-blue-50 border-blue-200 text-blue-800', iconColor: 'text-blue-600' },
    })[props.type],
);
</script>

<template>
  <div
    v-if="visible"
    :class="['flex items-start gap-3 rounded-lg border px-4 py-3 text-sm', config.wrap]"
    role="alert"
  >
    <component
      :is="config.icon"
      :class="['mt-0.5 h-5 w-5 shrink-0', config.iconColor]"
      aria-hidden="true"
    />
    <div class="flex-1">
      <slot />
    </div>
    <button
      v-if="dismissible"
      type="button"
      class="shrink-0 rounded p-0.5 hover:bg-black/5"
      aria-label="Dismiss"
      @click="visible = false"
    >
      <X class="h-4 w-4" />
    </button>
  </div>
</template>
