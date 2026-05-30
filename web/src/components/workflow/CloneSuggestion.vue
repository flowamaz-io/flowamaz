<script setup lang="ts">
defineProps<{
  workflowName: string;
  similarityScore: number;
}>();

defineEmits<{
  clone: [];
  dismiss: [];
}>();
</script>

<template>
  <div class="bg-violet-50 border border-violet-200 rounded-2xl p-4 flex items-start gap-3">
    <span class="text-xl shrink-0">🧬</span>
    <div class="flex-1 min-w-0">
      <p class="text-sm font-medium text-violet-900">
        Similar to <strong>{{ workflowName }}</strong> ({{ similarityScore }}% match)
      </p>
      <p class="text-xs text-violet-700 mt-0.5">
        Clone it and customise instead of starting from scratch.
      </p>
      <div class="flex gap-2 mt-3">
        <button
          class="text-xs bg-violet-600 hover:bg-violet-500 text-white rounded-lg px-3 py-1.5 font-medium"
          @click="$emit('clone')"
        >
          Clone it
        </button>
        <button
          class="text-xs text-violet-500 hover:text-violet-700"
          @click="$emit('dismiss')"
        >
          Start fresh
        </button>
      </div>
    </div>
  </div>
</template>
