<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-gray-100 mb-1">
        Create from Diagram or Whiteboard
      </h2>
      <p class="text-sm text-gray-400">
        Upload a photo of a whiteboard, sketch, or process diagram.
      </p>
    </div>

    <!-- Drop zone -->
    <div
      v-if="!processing"
      class="border-2 border-dashed border-gray-600 rounded-xl p-10 text-center cursor-pointer hover:border-indigo-500 transition-colors"
      :class="dragging ? 'border-indigo-400 bg-indigo-950/30' : ''"
      @dragover.prevent="dragging = true"
      @dragleave.prevent="dragging = false"
      @drop.prevent="onDrop"
      @click="fileInput?.click()"
    >
      <Camera class="w-10 h-10 mx-auto mb-3 text-gray-600" />
      <div class="text-sm text-gray-300 font-medium">
        Drop image here or click to upload
      </div>
      <div class="text-xs text-gray-500 mt-1">
        JPG, PNG, HEIC, PDF · Max 10MB
      </div>
      <input
        ref="fileInput"
        type="file"
        accept="image/jpeg,image/png,image/heic,application/pdf"
        class="hidden"
        @change="onFileSelect"
      >
    </div>

    <!-- Processing -->
    <div
      v-if="processing"
      class="text-center py-10"
    >
      <div class="w-8 h-8 border-4 border-indigo-500 border-t-transparent rounded-full animate-spin mx-auto mb-3" />
      <div class="text-sm text-indigo-400">
        Analysing your diagram... (15–20 seconds)
      </div>
    </div>

    <!-- Error -->
    <div
      v-if="error"
      class="p-3 bg-red-950 border border-red-700 rounded text-sm text-red-400"
    >
      {{ error }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { Camera } from 'lucide-vue-next';
import { createWorkflow } from '@/services/creation.service';

const props = defineProps<{ workspaceId: string; workflowName: string }>();

const router = useRouter();
const dragging = ref(false);
const processing = ref(false);
const error = ref<string | null>(null);
const fileInput = ref<HTMLInputElement | null>(null);

let lastFile: File | null = null;

async function processFile(file: File) {
  if (file.size > 10 * 1024 * 1024) {
    error.value = 'File too large. Maximum size is 10MB. Please resize the image and try again.';
    return;
  }

  lastFile = file;
  processing.value = true;
  error.value = null;

  try {
    const resized = await resizeImage(file, 1920);
    const imageBase64 = await fileToBase64(resized);
    const result = await createWorkflow(props.workspaceId, {
      name: props.workflowName || 'Untitled Workflow',
      method: 'visual',
      imageBase64,
      imageMimeType: 'image/jpeg',
    });
    await router.push({ name: 'workflow-editor', params: { id: result.workflowId }, query: { workspaceId: props.workspaceId } });
  } catch (e) {
    const msg = (e as { response?: { data?: { message?: string } } }).response?.data?.message;
    error.value = msg ?? 'Image processing failed. Please try again with a clearer photo.';
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

function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve((reader.result as string).split(',')[1]);
    reader.onerror = reject;
    reader.readAsDataURL(file);
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

defineExpose({ retrigger: () => { if (lastFile) processFile(lastFile); } });
</script>
