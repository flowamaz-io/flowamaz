<script setup lang="ts">
import { ref } from 'vue';
import { useFloating, offset, flip, shift, autoUpdate } from '@floating-ui/vue';
import { HelpCircle } from 'lucide-vue-next';
import { useHelp } from '@/composables/useHelp';

const props = defineProps<{
  /** Field description shown in the tooltip. */
  description: string;
  /** Help article slug opened by the "Learn more" link. */
  article?: string;
}>();

const open = ref(false);
const reference = ref<HTMLElement | null>(null);
const floating = ref<HTMLElement | null>(null);
const help = useHelp();

const { floatingStyles } = useFloating(reference, floating, {
  placement: 'top',
  middleware: [offset(8), flip(), shift({ padding: 8 })],
  whileElementsMounted: autoUpdate,
});

function learnMore(): void {
  open.value = false;
  if (props.article) help.openArticle(props.article);
}
</script>

<template>
  <span class="relative inline-flex items-center">
    <button
      ref="reference"
      type="button"
      class="inline-flex text-slate-400 transition-colors hover:text-primary-600 focus:outline-none focus-visible:text-primary-600"
      :aria-label="`Help: ${description}`"
      @click="open = !open"
      @mouseenter="open = true"
      @mouseleave="open = false"
    >
      <HelpCircle class="h-3.5 w-3.5" />
    </button>
    <Teleport to="body">
      <div
        v-if="open"
        ref="floating"
        :style="floatingStyles"
        class="z-[60] max-w-xs rounded-lg border border-slate-200 bg-white p-3 text-xs text-slate-600 shadow-lg"
        @mouseenter="open = true"
        @mouseleave="open = false"
      >
        <p>{{ description }}</p>
        <button
          v-if="article"
          type="button"
          class="mt-2 font-medium text-primary-600 hover:text-primary-700"
          @click="learnMore"
        >
          Learn more →
        </button>
      </div>
    </Teleport>
  </span>
</template>
