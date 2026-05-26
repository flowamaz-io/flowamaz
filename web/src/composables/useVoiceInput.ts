import { ref, onUnmounted } from 'vue';

export function useVoiceInput(lang?: string) {
  const isListening = ref(false);
  const transcript = ref('');
  const error = ref<string | null>(null);

  const w = window as unknown as Record<string, unknown>;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const SpeechRecognition = (w['SpeechRecognition'] ?? w['webkitSpeechRecognition']) as (new () => any) | undefined;

  const isSupported = SpeechRecognition != null;

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let recognition: any = null;

  function startListening() {
    if (!isSupported) {
      error.value = 'Voice input is not supported in this browser. Use Chrome or Edge for voice input.';
      return;
    }
    if (isListening.value) return;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    recognition = new SpeechRecognition();
    recognition.lang = lang ?? navigator.language ?? 'en-US';
    recognition.continuous = false;
    recognition.interimResults = false;

    recognition.onstart = () => { isListening.value = true; error.value = null; };
    recognition.onend = () => { isListening.value = false; };
    recognition.onerror = (e: { error: string }) => {
      isListening.value = false;
      error.value = e.error === 'not-allowed'
        ? 'Microphone access denied. Allow microphone permission and try again.'
        : `Voice recognition error: ${e.error}. Please try again.`;
    };
    recognition.onresult = (e: { results: { [k: number]: { [k: number]: { transcript: string } } } }) => {
      transcript.value = e.results[0][0].transcript;
    };

    recognition.start();
  }

  function stopListening() {
    if (!recognition || !isListening.value) return;
    recognition.stop();
  }

  function clearTranscript() { transcript.value = ''; }

  onUnmounted(() => { recognition?.stop(); });

  return { isListening, transcript, error, isSupported, startListening, stopListening, clearTranscript };
}
