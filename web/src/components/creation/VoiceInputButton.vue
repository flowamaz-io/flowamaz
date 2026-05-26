<template>
  <button
    type="button"
    :class="[
      'p-1.5 rounded transition-colors',
      isListening
        ? 'bg-red-500 text-white hover:bg-red-600 animate-pulse'
        : 'text-gray-400 hover:text-indigo-400 hover:bg-gray-700',
      !isSupported && 'opacity-40 cursor-not-allowed'
    ]"
    :title="isSupported ? (isListening ? 'Stop recording' : 'Start voice input') : 'Voice input not supported in this browser'"
    :disabled="!isSupported"
    @click="toggle"
  >
    <Mic class="w-4 h-4" v-if="!isListening" />
    <MicOff class="w-4 h-4" v-else />
  </button>
</template>

<script setup lang="ts">
import { watch } from 'vue';
import { Mic, MicOff } from 'lucide-vue-next';
import { useVoiceInput } from '@/composables/useVoiceInput';

const emit = defineEmits<{ transcript: [text: string] }>();

const { isListening, transcript, isSupported, startListening, stopListening } = useVoiceInput();

function toggle() {
  if (isListening.value) stopListening();
  else startListening();
}

watch(transcript, (text) => {
  if (text) emit('transcript', text);
});
</script>
