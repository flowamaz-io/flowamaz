<script setup lang="ts">
import { computed } from 'vue';
import { ChevronLeft, ChevronRight } from 'lucide-vue-next';

const props = defineProps<{
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}>();

const emit = defineEmits<{ 'page-change': [page: number] }>();

const rangeStart = computed(() => (props.total === 0 ? 0 : (props.page - 1) * props.pageSize + 1));
const rangeEnd = computed(() => Math.min(props.page * props.pageSize, props.total));

function go(page: number): void {
  if (page >= 1 && page <= props.totalPages && page !== props.page) emit('page-change', page);
}
</script>

<template>
  <div class="flex items-center justify-between text-sm text-slate-600">
    <span>{{ rangeStart }}–{{ rangeEnd }} of {{ total }}</span>
    <div class="flex items-center gap-1">
      <button
        type="button"
        class="rounded-lg border border-slate-300 p-1.5 disabled:opacity-40 hover:bg-slate-50"
        :disabled="page <= 1"
        aria-label="Previous page"
        @click="go(page - 1)"
      >
        <ChevronLeft class="h-4 w-4" />
      </button>
      <span class="px-2">Page {{ page }} of {{ Math.max(totalPages, 1) }}</span>
      <button
        type="button"
        class="rounded-lg border border-slate-300 p-1.5 disabled:opacity-40 hover:bg-slate-50"
        :disabled="page >= totalPages"
        aria-label="Next page"
        @click="go(page + 1)"
      >
        <ChevronRight class="h-4 w-4" />
      </button>
    </div>
  </div>
</template>
