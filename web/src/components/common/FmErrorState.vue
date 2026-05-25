<script setup lang="ts">
import { AlertTriangle } from 'lucide-vue-next';
import FmButton from './FmButton.vue';
import { useHelp } from '@/composables/useHelp';

// FmErrorState NEVER renders "Something went wrong" — it states what happened, why, and the
// next step, plus an optional help article (CLAUDE.md rule 8).
const props = defineProps<{
  title: string;
  description: string;
  actionLabel?: string;
  helpArticle?: string;
}>();

defineEmits<{ action: [] }>();

const help = useHelp();
function openHelp(): void {
  if (props.helpArticle) help.openArticle(props.helpArticle);
}
</script>

<template>
  <div class="flex flex-col items-center justify-center rounded-xl border border-danger-200 bg-danger-50 px-6 py-10 text-center">
    <div class="mb-3 flex h-11 w-11 items-center justify-center rounded-full bg-danger-100 text-danger-600">
      <AlertTriangle class="h-6 w-6" />
    </div>
    <h3 class="text-base font-semibold text-slate-900">
      {{ title }}
    </h3>
    <p class="mt-1 max-w-md text-sm text-slate-600">
      {{ description }}
    </p>
    <div class="mt-5 flex items-center gap-3">
      <FmButton
        v-if="actionLabel"
        variant="secondary"
        @click="$emit('action')"
      >
        {{ actionLabel }}
      </FmButton>
      <button
        v-if="helpArticle"
        type="button"
        class="text-sm font-medium text-primary-600 hover:text-primary-700"
        @click="openHelp"
      >
        Read the help article →
      </button>
    </div>
  </div>
</template>
