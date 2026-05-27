<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useDebounceFn } from '@vueuse/core';
import CreationMethodSelector from '../../components/creation/CreationMethodSelector.vue';
import NlTemplateForm from '../../components/creation/NlTemplateForm.vue';
import VoiceInputButton from '../../components/creation/VoiceInputButton.vue';
import VisualInputPanel from '../../components/creation/VisualInputPanel.vue';
import ConversationImportPanel from '../../components/creation/ConversationImportPanel.vue';
import DocumentUploadPanel from '../../components/creation/DocumentUploadPanel.vue';
import CloneSuggestion from '../../components/workflow/CloneSuggestion.vue';
import { workflowService } from '../../services/workflow.service';
import { useWorkspace } from '../../composables/useWorkspace';
import type { SopParseResult } from '../../services/creation.service';

const route = useRoute();
const router = useRouter();
const ws = useWorkspace();
const workspaceId = ws.currentWorkspaceId;

const selectedMethod = ref<string | null>(null);
const generatedYaml = ref<string | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);
const workflowName = ref('');
const cloneSuggestion = ref<{ id: string; name: string; similarityScore: number } | null>(null);
const cloneDismissed = ref(false);

const checkCloneSuggestion = useDebounceFn(async (name: string) => {
  if (cloneDismissed.value || name.length < 3 || !workspaceId.value) { cloneSuggestion.value = null; return; }
  cloneSuggestion.value = await workflowService.suggestClone(workspaceId.value, name);
}, 600);

async function cloneWorkflow() {
  if (!cloneSuggestion.value || !workspaceId.value) return;
  loading.value = true;
  try {
    const cloned = await workflowService.clone(workspaceId.value, cloneSuggestion.value.id);
    await router.push({ name: 'workflow-editor', params: { id: cloned.id } });
  } catch (e: unknown) {
    error.value = `Clone failed. ${e instanceof Error ? e.message : 'Please try again.'}`;
    loading.value = false;
  }
}

function onYamlGenerated(yaml: string) {
  generatedYaml.value = yaml;
  error.value = null;
}

function onDocumentParsed(result: SopParseResult) {
  generatedYaml.value = result.yaml_content;
  error.value = null;
}

function onConversationYaml(yaml: string) {
  generatedYaml.value = yaml;
  error.value = null;
}

onMounted(() => {
  const method = route.query.method as string | undefined;
  if (method) selectedMethod.value = method;
});
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-8 space-y-8">
    <!-- Header -->
    <div>
      <h1 class="text-2xl font-bold text-neutral-900">Create a new workflow</h1>
      <p class="text-neutral-500 mt-1">Choose how you'd like to define your process.</p>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center justify-center py-20">
      <div class="w-8 h-8 border-4 border-violet-600 border-t-transparent rounded-full animate-spin" />
    </div>

    <!-- Error -->
    <div v-else-if="error" class="bg-red-50 border border-red-200 rounded-xl px-4 py-3 text-sm text-red-700">
      {{ error }}
    </div>

    <template v-else>
      <!-- Method selector -->
      <section v-if="!selectedMethod" class="space-y-4">
        <!-- Workflow name + clone suggestion -->
        <div class="space-y-2">
          <label class="text-sm font-medium text-neutral-700">Workflow name</label>
          <input
            v-model="workflowName"
            type="text"
            placeholder="e.g. Invoice Approval, Customer Onboarding..."
            class="w-full border border-neutral-200 rounded-xl px-4 py-2.5 text-sm focus:border-violet-500 focus:outline-none"
            @input="checkCloneSuggestion(workflowName)"
          />
        </div>
        <CloneSuggestion
          v-if="cloneSuggestion && !cloneDismissed"
          :workflow-name="cloneSuggestion.name"
          :similarity-score="cloneSuggestion.similarityScore"
          @clone="cloneWorkflow"
          @dismiss="cloneDismissed = true; cloneSuggestion = null"
        />
        <CreationMethodSelector v-model="selectedMethod" />
      </section>

      <!-- Active creation method -->
      <section v-else class="space-y-6">
        <button
          class="text-sm text-neutral-500 hover:text-violet-600 flex items-center gap-1"
          @click="selectedMethod = null; generatedYaml = null"
        >
          ← Back to methods
        </button>

        <NlTemplateForm
          v-if="selectedMethod === 'nl'"
          :workspace-id="workspaceId ?? ''"
          @generated="onYamlGenerated"
        />

        <div v-else-if="selectedMethod === 'voice'" class="space-y-4">
          <p class="text-sm text-neutral-600">Speak your workflow description. Then switch to the form to refine it.</p>
          <VoiceInputButton />
        </div>

        <VisualInputPanel
          v-else-if="selectedMethod === 'visual'"
          :workspace-id="workspaceId ?? ''"
          @generated="onYamlGenerated"
        />

        <ConversationImportPanel
          v-else-if="selectedMethod === 'conversation'"
          :workspace-id="workspaceId ?? ''"
          @generated="onConversationYaml"
        />

        <DocumentUploadPanel
          v-else-if="selectedMethod === 'document'"
          :workspace-id="workspaceId ?? ''"
          @parsed="onDocumentParsed"
        />

        <div v-else-if="selectedMethod === 'canvas'" class="bg-neutral-50 rounded-2xl p-10 text-center text-neutral-400">
          <p>Canvas editor opens here — available in the next step.</p>
        </div>
      </section>

      <!-- Generated YAML preview -->
      <section v-if="generatedYaml" class="bg-neutral-900 rounded-2xl p-4">
        <p class="text-xs text-neutral-400 mb-2 uppercase tracking-wide">Generated Workflow YAML</p>
        <pre class="text-xs text-green-400 overflow-auto max-h-80 whitespace-pre-wrap">{{ generatedYaml }}</pre>
      </section>
    </template>
  </div>
</template>
