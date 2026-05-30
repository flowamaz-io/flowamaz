<script setup lang="ts">
import { computed } from 'vue';
import { Download, Eye } from 'lucide-vue-next';
import type { TemplateListItem } from '@/services/template.service';

const props = defineProps<{ template: TemplateListItem }>();
const emit = defineEmits<{ preview: [id: string]; install: [id: string] }>();

const categoryEmoji: Record<string, string> = {
  finance: '💰',
  hr: '👥',
  it: '🖥️',
  legal: '⚖️',
  operations: '⚙️',
  custom: '🧩',
};

const icon = computed(() => categoryEmoji[props.template.category.toLowerCase()] ?? '📋');

const installLabel = computed<string>(() => {
  const n = props.template.installCount;
  if (n >= 1000) return `${(n / 1000).toFixed(1)}k`;
  return String(n);
});
</script>

<template>
  <div
    class="flex cursor-pointer flex-col gap-3 rounded-xl border border-slate-200 bg-white p-4 transition-shadow hover:shadow-md"
    @click="emit('preview', template.id)"
  >
    <!-- Preview image or category glyph -->
    <div class="flex h-24 items-center justify-center overflow-hidden rounded-lg bg-slate-50">
      <img
        v-if="template.previewImageUrl"
        :src="template.previewImageUrl"
        :alt="template.name"
        class="h-full w-full object-cover"
      >
      <span
        v-else
        class="text-4xl leading-none"
      >{{ icon }}</span>
    </div>

    <!-- Name + badges -->
    <div class="flex items-start justify-between gap-2">
      <p class="font-semibold text-slate-900">
        {{ template.name }}
      </p>
      <span
        v-if="template.isOfficial"
        class="shrink-0 rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-700"
      >
        Official
      </span>
      <span
        v-else
        class="shrink-0 rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700"
      >
        Community
      </span>
    </div>

    <p class="text-sm leading-snug text-slate-500">
      {{ template.description }}
    </p>

    <span class="w-fit rounded-full bg-slate-100 px-2 py-0.5 text-xs capitalize text-slate-600">
      {{ template.category }}
    </span>

    <!-- Footer: installs + actions -->
    <div class="mt-auto flex items-center justify-between pt-1">
      <span class="text-xs text-slate-400">{{ installLabel }} installs</span>
      <div class="flex items-center gap-2">
        <button
          class="flex items-center gap-1 rounded-lg border border-slate-200 px-3 py-1.5 text-xs font-semibold text-slate-600 transition-colors hover:bg-slate-50"
          @click.stop="emit('preview', template.id)"
        >
          <Eye class="h-3.5 w-3.5" />
          Preview
        </button>
        <button
          class="flex items-center gap-1 rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-indigo-700"
          @click.stop="emit('install', template.id)"
        >
          <Download class="h-3.5 w-3.5" />
          Install
        </button>
      </div>
    </div>
  </div>
</template>
