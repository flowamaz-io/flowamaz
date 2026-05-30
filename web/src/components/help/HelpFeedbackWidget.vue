<script setup lang="ts">
import { ref } from 'vue';
import { ThumbsUp, ThumbsDown } from 'lucide-vue-next';
import { helpService } from '@/services/help.service';

const props = defineProps<{ slug: string }>();
const submitted = ref(false);

async function vote(helpful: boolean): Promise<void> {
  try {
    await helpService.submitFeedback(props.slug, helpful);
  } catch {
    // Feedback is best-effort.
  }
  submitted.value = true;
}
</script>

<template>
  <div class="mt-5 border-t border-slate-100 pt-4">
    <p
      v-if="submitted"
      class="text-sm text-slate-500"
      data-test="feedback-thanks"
    >
      Thanks for your feedback!
    </p>
    <div
      v-else
      class="flex items-center gap-3"
    >
      <span class="text-sm text-slate-500">Was this helpful?</span>
      <button
        type="button"
        class="rounded-lg border border-slate-200 p-1.5 text-slate-500 hover:bg-slate-50 hover:text-primary-600"
        aria-label="Yes, helpful"
        data-test="feedback-up"
        @click="vote(true)"
      >
        <ThumbsUp class="h-4 w-4" />
      </button>
      <button
        type="button"
        class="rounded-lg border border-slate-200 p-1.5 text-slate-500 hover:bg-slate-50 hover:text-red-500"
        aria-label="No, not helpful"
        data-test="feedback-down"
        @click="vote(false)"
      >
        <ThumbsDown class="h-4 w-4" />
      </button>
    </div>
  </div>
</template>
