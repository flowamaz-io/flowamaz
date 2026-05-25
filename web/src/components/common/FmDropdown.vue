<script setup lang="ts">
import { computed, ref } from 'vue';
import { onClickOutside } from '@vueuse/core';

const props = withDefaults(
  defineProps<{ position?: 'bottom-left' | 'bottom-right' }>(),
  { position: 'bottom-left' },
);

const open = ref(false);
const root = ref<HTMLElement | null>(null);

onClickOutside(root, () => (open.value = false));

const menuClass = computed(() =>
  props.position === 'bottom-right' ? 'right-0 origin-top-right' : 'left-0 origin-top-left',
);

function toggle(): void {
  open.value = !open.value;
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') open.value = false;
}

function close(): void {
  open.value = false;
}

defineExpose({ close });
</script>

<template>
  <div
    ref="root"
    class="relative inline-block"
    @keydown="onKeydown"
  >
    <div @click="toggle">
      <slot
        name="trigger"
        :open="open"
      />
    </div>
    <Transition
      enter-active-class="transition duration-100 ease-out"
      enter-from-class="opacity-0 scale-95"
      leave-active-class="transition duration-75 ease-in"
      leave-to-class="opacity-0 scale-95"
    >
      <div
        v-if="open"
        :class="[
          'absolute z-40 mt-2 min-w-[12rem] rounded-lg border border-slate-200 bg-white py-1 shadow-lg',
          menuClass,
        ]"
        role="menu"
        @click="close"
      >
        <slot name="items" />
      </div>
    </Transition>
  </div>
</template>
