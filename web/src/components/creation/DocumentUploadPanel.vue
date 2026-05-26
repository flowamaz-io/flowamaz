<script setup lang="ts">
import { ref } from 'vue';
import { creationService } from '../../services/creation.service';
import type { SopParseResult } from '../../services/creation.service';

const props = defineProps<{ workspaceId: string }>();
const emit = defineEmits<{ parsed: [result: SopParseResult] }>();

const dragging = ref(false);
const loading = ref(false);
const error = ref<string | null>(null);
const result = ref<SopParseResult | null>(null);

const ALLOWED_TYPES = ['application/pdf', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'text/plain'];
const MAX_BYTES = 20 * 1024 * 1024;

function onDragOver(e: DragEvent) {
  e.preventDefault();
  dragging.value = true;
}
function onDragLeave() {
  dragging.value = false;
}
function onDrop(e: DragEvent) {
  e.preventDefault();
  dragging.value = false;
  const file = e.dataTransfer?.files[0];
  if (file) handleFile(file);
}
function onFileInput(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0];
  if (file) handleFile(file);
}

async function handleFile(file: File) {
  error.value = null;
  if (!ALLOWED_TYPES.includes(file.type)) {
    error.value = `Unsupported file type. Upload a PDF, DOCX, or TXT file.`;
    return;
  }
  if (file.size > MAX_BYTES) {
    error.value = `File too large (${(file.size / 1024 / 1024).toFixed(1)}MB). Maximum is 20MB.`;
    return;
  }
  loading.value = true;
  try {
    result.value = await creationService.parseDocument(props.workspaceId, file);
    emit('parsed', result.value);
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e);
    error.value = `Document parsing failed. ${msg}. Try re-uploading or use a different file.`;
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <div class="space-y-4">
    <div
      :class="[
        'border-2 border-dashed rounded-xl p-10 text-center cursor-pointer transition-colors',
        dragging ? 'border-violet-500 bg-violet-50' : 'border-neutral-200 hover:border-violet-300',
      ]"
      @dragover="onDragOver"
      @dragleave="onDragLeave"
      @drop="onDrop"
      @click="($refs.fileInput as HTMLInputElement)?.click()"
    >
      <input ref="fileInput" type="file" accept=".pdf,.docx,.txt" class="hidden" @change="onFileInput" />
      <p v-if="!loading" class="text-sm text-neutral-500">
        Drag a <strong>PDF</strong>, <strong>DOCX</strong>, or <strong>TXT</strong> file here,<br />or click to browse — up to 20MB
      </p>
      <p v-else class="text-sm text-violet-600 animate-pulse">Parsing document…</p>
    </div>

    <p v-if="error" class="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{{ error }}</p>

    <div v-if="result" class="bg-neutral-50 rounded-xl p-4 space-y-2">
      <p class="text-sm font-medium text-neutral-700">
        Parsed {{ result.page_count }} page(s) · {{ result.word_count.toLocaleString() }} words · {{ result.tokens_used.toLocaleString() }} tokens
      </p>
      <p class="text-xs text-neutral-500 font-medium uppercase tracking-wide">Extracted Steps</p>
      <ol class="list-decimal list-inside space-y-1">
        <li v-for="(step, i) in result.extracted_steps" :key="i" class="text-sm text-neutral-700">{{ step }}</li>
      </ol>
    </div>
  </div>
</template>
