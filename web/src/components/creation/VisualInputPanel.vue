<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-gray-100 mb-1">Create from Diagram or Whiteboard</h2>
      <p class="text-sm text-gray-400">Upload a photo of a whiteboard, sketch, or process diagram.</p>
    </div>

    <!-- Drop zone -->
    <div
      v-if="!result && !processing"
      class="border-2 border-dashed border-gray-600 rounded-xl p-10 text-center cursor-pointer hover:border-indigo-500 transition-colors"
      :class="dragging ? 'border-indigo-400 bg-indigo-950/30' : ''"
      @dragover.prevent="dragging = true"
      @dragleave.prevent="dragging = false"
      @drop.prevent="onDrop"
      @click="fileInput?.click()"
    >
      <Camera class="w-10 h-10 mx-auto mb-3 text-gray-600" />
      <div class="text-sm text-gray-300 font-medium">Drop image here or click to upload</div>
      <div class="text-xs text-gray-500 mt-1">JPG, PNG, HEIC, PDF · Max 10MB</div>
      <input ref="fileInput" type="file" accept="image/jpeg,image/png,image/heic,application/pdf" class="hidden" @change="onFileSelect" />
    </div>

    <!-- Processing -->
    <div v-if="processing" class="text-center py-10">
      <div class="text-sm text-indigo-400 animate-pulse">Analysing your diagram with AI...</div>
      <div class="text-xs text-gray-500 mt-1">This usually takes 5-15 seconds</div>
    </div>

    <!-- Error -->
    <div v-if="error" class="p-3 bg-red-950 border border-red-700 rounded text-sm text-red-400">
      {{ error }}
    </div>

    <!-- Result -->
    <template v-if="result && !processing">
      <div class="grid grid-cols-2 gap-4">
        <!-- Original image preview -->
        <div class="space-y-1">
          <div class="text-xs text-gray-400 font-medium">Original image</div>
          <img :src="imageUrl" class="w-full rounded border border-gray-700 max-h-64 object-contain" />
          <div class="text-xs text-gray-600">Stored as workflow reference</div>
        </div>

        <!-- Detected elements -->
        <div class="space-y-2">
          <div class="text-xs text-gray-400 font-medium">Detected elements</div>
          <div class="text-xs text-gray-300">
            {{ result.yamlDraft ? 'Workflow extracted. Review below.' : 'No elements detected.' }}
          </div>
          <div class="text-xs text-green-400">Cost: ~${{ result.costUsd?.toFixed(3) }}</div>
        </div>
      </div>

      <!-- Low-confidence confirmation -->
      <VisualConfirmationStep
        v-if="result.lowConfidenceElements?.length"
        :elements="result.lowConfidenceElements"
        @apply="applyToCanvas"
        @confirmed="onCorrections"
      />
      <button
        v-else
        class="w-full py-2 px-4 rounded bg-indigo-600 hover:bg-indigo-500 text-sm font-medium text-white transition-colors"
        @click="applyToCanvas"
      >
        Apply to Canvas
      </button>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { Camera } from 'lucide-vue-next';
import VisualConfirmationStep from './VisualConfirmationStep.vue';
import axios from 'axios';

interface VisualResult {
  yamlDraft: string;
  lowConfidenceElements: Array<{ id: string; label: string; detectedType: string; confidence: number; annotations: string[] }>;
  costUsd: number;
  tokensUsed: number;
}

const props = defineProps<{ workspaceId: string }>();
const emit = defineEmits<{ generated: [yaml: string]; close: [] }>();

const dragging = ref(false);
const processing = ref(false);
const error = ref<string | null>(null);
const result = ref<VisualResult | null>(null);
const imageUrl = ref('');
const fileInput = ref<HTMLInputElement | null>(null);
function onCorrections(_c: Record<string, string>) { /* corrections available for future API call */ }

async function processFile(file: File) {
  if (file.size > 10 * 1024 * 1024) {
    error.value = 'File too large. Maximum size is 10MB. Please resize the image and try again.';
    return;
  }

  imageUrl.value = URL.createObjectURL(file);
  processing.value = true;
  error.value = null;
  result.value = null;

  // Client-side resize: cap at 1920px width using Canvas API
  const resized = await resizeImage(file, 1920);

  const form = new FormData();
  form.append('image', resized, file.name);

  try {
    const { data } = await axios.post<VisualResult>(
      `/api/v1/workspaces/${props.workspaceId}/workflows/from-image`,
      form,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    result.value = data;
  } catch (e) {
    const msg = (e as { response?: { data?: { error?: string } } }).response?.data?.error;
    error.value = msg ?? 'Image processing failed. Please try again with a clearer photo.';
  } finally {
    processing.value = false;
  }
}

async function resizeImage(file: File, maxPx: number): Promise<File> {
  if (!file.type.startsWith('image/')) return file;
  return new Promise((resolve) => {
    const img = new Image();
    img.onload = () => {
      const scale = Math.min(1, maxPx / Math.max(img.width, img.height));
      const canvas = document.createElement('canvas');
      canvas.width = Math.round(img.width * scale);
      canvas.height = Math.round(img.height * scale);
      canvas.getContext('2d')!.drawImage(img, 0, 0, canvas.width, canvas.height);
      canvas.toBlob(blob => resolve(new File([blob!], file.name, { type: 'image/jpeg' })), 'image/jpeg', 0.92);
    };
    img.src = URL.createObjectURL(file);
  });
}

function onDrop(e: DragEvent) {
  dragging.value = false;
  const file = e.dataTransfer?.files[0];
  if (file) processFile(file);
}

function onFileSelect(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0];
  if (file) processFile(file);
}

function applyToCanvas() {
  if (result.value?.yamlDraft) emit('generated', result.value.yamlDraft);
}
</script>
