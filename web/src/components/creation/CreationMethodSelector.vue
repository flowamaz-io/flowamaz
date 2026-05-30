<script setup lang="ts">
defineProps<{ modelValue: string | null }>();
defineEmits<{ 'update:modelValue': [value: string] }>();

const methods = [
  { id: 'nl', icon: '✍️', label: 'Write a description', description: 'Describe your workflow in plain English using a guided form.' },
  { id: 'voice', icon: '🎤', label: 'Speak it', description: 'Use your microphone to describe the process out loud.' },
  { id: 'visual', icon: '📷', label: 'Upload a photo', description: 'Take a photo of a whiteboard or diagram and let AI parse it.' },
  { id: 'conversation', icon: '💬', label: 'Paste a conversation', description: 'Import a Slack thread, email chain, or meeting notes.' },
  { id: 'document', icon: '📄', label: 'Upload a document', description: 'Import a SOP, policy PDF, or DOCX and generate a workflow.' },
  { id: 'canvas', icon: '🎨', label: 'Draw on canvas', description: 'Start from a blank canvas and build your flow visually.' },
];
</script>

<template>
  <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
    <button
      v-for="method in methods"
      :key="method.id"
      :class="[
        'text-left rounded-2xl border-2 p-5 transition-all focus:outline-none focus:ring-2 focus:ring-violet-500',
        modelValue === method.id
          ? 'border-violet-600 bg-violet-50 shadow-md'
          : 'border-neutral-200 hover:border-violet-300 hover:bg-neutral-50',
      ]"
      @click="$emit('update:modelValue', method.id)"
    >
      <span class="text-2xl mb-3 block">{{ method.icon }}</span>
      <p class="font-semibold text-neutral-800 text-sm">
        {{ method.label }}
      </p>
      <p class="text-xs text-neutral-500 mt-1 leading-relaxed">
        {{ method.description }}
      </p>
    </button>
  </div>
</template>
