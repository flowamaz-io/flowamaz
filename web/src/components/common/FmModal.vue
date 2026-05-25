<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue';
import { X } from 'lucide-vue-next';

const props = withDefaults(
  defineProps<{ modelValue: boolean; size?: 'sm' | 'md' | 'lg'; title?: string }>(),
  { size: 'md' },
);

const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>();

const sizeClass = computed(
  () => ({ sm: 'max-w-sm', md: 'max-w-lg', lg: 'max-w-2xl' })[props.size],
);

function close(): void {
  emit('update:modelValue', false);
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && props.modelValue) close();
}

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
          :class="['w-full rounded-xl bg-white shadow-xl', sizeClass]"
          role="dialog"
          aria-modal="true"
        >
          <div class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
            <slot name="header">
              <h2 class="text-base font-semibold text-slate-900">
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
