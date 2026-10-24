<template>
  <div class="space-y-4">
    <p
      class="text-sm text-gray-300"
      data-test="review-summary"
    >
      We found {{ items.length }} step{{ items.length === 1 ? '' : 's' }}.
      <template v-if="flagged.length > 0">
        {{ flagged.length }} {{ flagged.length === 1 ? 'was' : 'were' }} hard to read — check
        {{ flagged.length === 1 ? 'it' : 'them' }} before creating the workflow.
      </template>
      <template v-else>
        All were read clearly. Rename or remove any step, then create the workflow.
      </template>
    </p>

    <ul class="space-y-2">
      <li
        v-for="item in ordered"
        :key="item.id"
        :data-test="`review-item-${item.id}`"
        :class="[
          'rounded-lg border p-3 transition-colors',
          !item.keep
            ? 'border-gray-700 bg-gray-900 opacity-60'
            : item.reviewNeeded
              ? 'border-amber-600 bg-amber-950/40'
              : 'border-gray-700 bg-gray-800',
        ]"
      >
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-xs font-mono uppercase tracking-wide text-gray-400">{{ item.type }}</span>
          <span
            :class="[
              'text-xs font-medium rounded px-1.5 py-0.5',
              item.reviewNeeded ? 'bg-amber-700 text-amber-50' : 'bg-gray-700 text-gray-300',
            ]"
            :data-test="`confidence-${item.id}`"
          >
            {{ formatConfidence(item.confidence) }} sure
          </span>
          <span
            v-if="!item.keep"
            class="text-xs text-gray-500"
          >Will be removed — connected steps are joined up.</span>
        </div>

        <p
          v-if="item.reviewNeeded && item.reason"
          class="mt-1 text-xs text-amber-300"
          :data-test="`reason-${item.id}`"
        >
          {{ item.reason }}
        </p>

        <div class="mt-2 flex flex-wrap items-center gap-2">
          <label
            :for="`review-label-${item.id}`"
            class="sr-only"
          >Step name</label>
          <input
            :id="`review-label-${item.id}`"
            :value="item.label"
            :disabled="!item.keep"
            :data-test="`label-${item.id}`"
            placeholder="Name this step"
            class="min-w-0 flex-1 rounded border border-gray-600 bg-gray-900 px-2 py-1 text-sm text-gray-100 outline-none focus:border-indigo-500 disabled:cursor-not-allowed"
            @input="rename(item.id, ($event.target as HTMLInputElement).value)"
          >
          <button
            v-if="item.keep && item.label !== item.originalLabel"
            type="button"
            class="text-xs text-gray-400 hover:text-gray-200"
            @click="rename(item.id, item.originalLabel)"
          >
            Undo rename
          </button>
          <button
            type="button"
            :data-test="`toggle-${item.id}`"
            :class="[
              'rounded px-2 py-1 text-xs font-medium transition-colors',
              item.keep
                ? 'border border-gray-600 text-gray-300 hover:border-red-500 hover:text-red-300'
                : 'bg-indigo-600 text-white hover:bg-indigo-500',
            ]"
            @click="setKeep(item.id, !item.keep)"
          >
            {{ item.keep ? 'Remove' : 'Keep' }}
          </button>
        </div>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { formatConfidence, itemsNeedingReview, type ReviewItem } from '@/utils/visualReview.util';

const items = defineModel<ReviewItem[]>('items', { required: true });

const flagged = computed(() => itemsNeedingReview(items.value));

/** Flagged steps first (least confident first), then the rest in detection order. */
const ordered = computed(() => [
  ...flagged.value,
  ...items.value.filter((i) => !i.reviewNeeded),
]);

function update(id: string, patch: Partial<ReviewItem>) {
  items.value = items.value.map((i) => (i.id === id ? { ...i, ...patch } : i));
}

function rename(id: string, label: string) {
  update(id, { label });
}

function setKeep(id: string, keep: boolean) {
  update(id, { keep });
}
</script>
