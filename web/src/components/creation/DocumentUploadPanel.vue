<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { createWorkflow } from '@/services/creation.service';

const props = defineProps<{ workspaceId: string; workflowName: string }>();

const router = useRouter();
const dragging = ref(false);
const loading = ref(false);
const error = ref<string | null>(null);

const ALLOWED_TYPES = ['application/pdf', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'text/plain'];
const MAX_BYTES = 20 * 1024 * 1024;

let lastFile: File | null = null;

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

function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve((reader.result as string).split(',')[1]);
    reader.onerror = reject;
    reader.readAsDataURL(file);
  });
}

async function handleFile(file: File) {
  error.value = null;
  if (!ALLOWED_TYPES.includes(file.type)) {
    error.value = 'Unsupported file type. Upload a PDF, DOCX, or TXT file.';
    return;
  }
  if (file.size > MAX_BYTES) {
    error.value = `File too large (${(file.size / 1024 / 1024).toFixed(1)}MB). Maximum is 20MB.`;
    return;
  }
  lastFile = file;
  loading.value = true;

  try {
    const documentBase64 = await fileToBase64(file);
    const result = await createWorkflow(props.workspaceId, {
      name: props.workflowName || 'Untitled Workflow',
      method: 'document',
      documentBase64,
      documentMimeType: file.type,
    });
    await router.push({ name: 'workflow-editor', params: { id: result.workflowId }, query: { workspaceId: props.workspaceId } });
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e);
    error.value = `Document parsing failed. ${msg}. Try re-uploading or use a different file.`;
    loading.value = false;
  }
}

defineExpose({ retrigger: () => { if (lastFile) handleFile(lastFile); } });
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
      <input
        ref="fileInput"
        type="file"
        accept=".pdf,.docx,.txt"
        class="hidden"
        @change="onFileInput"
      >
      <p
        v-if="!loading"
        class="text-sm text-neutral-500"
      >
        Drag a <strong>PDF</strong>, <strong>DOCX</strong>, or <strong>TXT</strong> file here,<br>or click to browse — up to 20MB
      </p>
      <div
        v-else
        class="space-y-2"
      >
        <div class="w-8 h-8 border-4 border-violet-500 border-t-transparent rounded-full animate-spin mx-auto" />
        <p class="text-sm text-violet-600">
          Parsing document... (20–30 seconds)
        </p>
      </div>
    </div>

    <p
      v-if="error"
      class="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2"
    >
      {{ error }}
    </p>
  </div>
</template>
