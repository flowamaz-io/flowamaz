<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue';
import { X } from 'lucide-vue-next';

const props = withDefaults(
  defineProps<{ modelValue: boolean; size?: 'sm' | 'md' | 'lg'; title?: string }>(),
  { size: 'md' },
);

const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>();

const titleId = useId();
const dialogRef = ref<HTMLElement | null>(null);
let previouslyFocused: HTMLElement | null = null;

const sizeClass = computed(
  () => ({ sm: 'max-w-sm', md: 'max-w-lg', lg: 'max-w-2xl' })[props.size],
);

function close(): void {
  emit('update:modelValue', false);
}

function focusableElements(): HTMLElement[] {
  if (!dialogRef.value) return [];
  return Array.from(
    dialogRef.value.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
    ),
  ).filter((el) => el.offsetParent !== null);
}

function onKeydown(event: KeyboardEvent): void {
  if (!props.modelValue) return;
  if (event.key === 'Escape') {
    close();
    return;
  }
  if (event.key === 'Tab') {
    const focusable = focusableElements();
    if (focusable.length === 0) {
      event.preventDefault();
      dialogRef.value?.focus();
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement | null;
    if (event.shiftKey) {
      if (active === first || !dialogRef.value?.contains(active)) {
        event.preventDefault();
        last.focus();
      }
    } else if (active === last || !dialogRef.value?.contains(active)) {
      event.preventDefault();
      first.focus();
    }
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      previouslyFocused = document.activeElement as HTMLElement | null;
      void nextTick(() => {
        const focusable = focusableElements();
        (focusable[0] ?? dialogRef.value)?.focus();
      });
    } else {
      previouslyFocused?.focus();
      previouslyFocused = null;
    }
  },
);

onMounted(() => document.addEventListener('keydown', onKeydown));
onBeforeUnmount(() => document.removeEventListener('keydown', onKeydown));
</script>

<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition duration-150 ease-out"
      enter-from-class="opacity-0"
      leave-active-class="transition duration-100 ease-in"
      leave-to-class="opacity-0"
    >
      <div
        v-if="modelValue"
        class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 p-4"
        @click.self="close"
      >
        <div
          ref="dialogRef"
          :class="['w-full rounded-xl bg-white shadow-xl', sizeClass]"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="titleId"
          tabindex="-1"
        >
          <div class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
            <slot name="header">
              <h2
                :id="titleId"
                class="text-base font-semibold text-slate-900"
              >
                {{ title }}
              </h2>
            </slot>
            <button
              type="button"
              class="rounded-lg p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
              aria-label="Close"
              @click="close"
            >
              <X class="h-5 w-5" />
            </button>
          </div>
          <div class="px-5 py-4">
            <slot />
          </div>
          <div
            v-if="$slots.footer"
            class="flex justify-end gap-2 border-t border-slate-200 px-5 py-4"
          >
            <slot name="footer" />
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
