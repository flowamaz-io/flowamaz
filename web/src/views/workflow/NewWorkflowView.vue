<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useDebounceFn } from '@vueuse/core';
import CreationMethodSelector from '../../components/creation/CreationMethodSelector.vue';
import NlTemplateForm from '../../components/creation/NlTemplateForm.vue';
import SfgCanvas from '../../components/canvas/SfgCanvas.vue';
import VoiceInputButton from '../../components/creation/VoiceInputButton.vue';
import VisualInputPanel from '../../components/creation/VisualInputPanel.vue';
import ConversationImportPanel from '../../components/creation/ConversationImportPanel.vue';
import DocumentUploadPanel from '../../components/creation/DocumentUploadPanel.vue';
import CloneSuggestion from '../../components/workflow/CloneSuggestion.vue';
import YamlResultPanel from '../../components/creation/YamlResultPanel.vue';
import { workflowService } from '../../services/workflow.service';
import { useWorkspace } from '../../composables/useWorkspace';
import type { SopParseResult } from '../../services/creation.service';
import type { WorkflowCreatedByMethod } from '@/types';

const route = useRoute();
const router = useRouter();
const ws = useWorkspace();
const workspaceId = ws.currentWorkspaceId;

const selectedMethod = ref<string | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);
const workflowName = ref('');
const cloneSuggestion = ref<{ id: string; name: string; similarityScore: number } | null>(null);
const cloneDismissed = ref(false);

const panelOpen = ref(false);
const panelYaml = ref('');
const panelName = ref('');
const panelMethod = ref<WorkflowCreatedByMethod>('NaturalLanguage');

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

function onNlGenerated(yaml: string, name: string) {
  panelYaml.value = yaml;
  panelName.value = name;
  panelMethod.value = 'NaturalLanguage';
  panelOpen.value = true;
  error.value = null;
}

function onYamlGenerated(yaml: string) {
  panelYaml.value = yaml;
  panelName.value = workflowName.value;
  panelOpen.value = true;
  error.value = null;
}

function onDocumentParsed(result: SopParseResult) {
  panelYaml.value = result.yaml_content;
  panelName.value = workflowName.value;
  panelMethod.value = 'Document';
  panelOpen.value = true;
  error.value = null;
}

function onConversationYaml(yaml: string) {
  panelYaml.value = yaml;
  panelName.value = workflowName.value;
  panelMethod.value = 'Conversation';
  panelOpen.value = true;
  error.value = null;
}

function openInCanvas(yaml: string) {
  localStorage.setItem('fmz_canvas_yaml', yaml);
  panelOpen.value = false;
  selectedMethod.value = 'canvas';
}

async function saveAsDraft(name: string, yaml: string) {
  if (!workspaceId.value) return;
  const slug = (name || 'untitled').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'untitled';
  loading.value = true;
  panelOpen.value = false;
  try {
    const created = await workflowService.create(workspaceId.value, {
      name: name || 'Untitled Workflow',
      slug,
      yamlContent: yaml,
      createdByMethod: panelMethod.value,
    });
    await router.push({ name: 'workflow-editor', params: { id: created.id } });
  } catch (e: unknown) {
    error.value = `Save failed. ${e instanceof Error ? e.message : 'Please try again.'}`;
    panelOpen.value = true;
  } finally {
    loading.value = false;
  }
}

const isCanvasMode = computed(() => selectedMethod.value === 'canvas');

onMounted(() => {
  const method = route.query.method as string | undefined;
  if (method) selectedMethod.value = method;
});
</script>

<template>
  <div :class="isCanvasMode ? 'flex flex-col h-full' : 'max-w-4xl mx-auto px-4 py-8 space-y-8'">
    <!-- Header — hidden in canvas mode -->
    <div v-if="!isCanvasMode">
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
      <section :class="isCanvasMode ? 'flex flex-col flex-1 overflow-hidden' : 'space-y-6'" v-else>
        <button
          v-if="!isCanvasMode"
          class="text-sm text-neutral-500 hover:text-violet-600 flex items-center gap-1"
          @click="selectedMethod = null"
        >
          ← Back to methods
        </button>

        <NlTemplateForm
          v-if="selectedMethod === 'nl'"
          :workspace-id="workspaceId ?? ''"
          @generated="onNlGenerated"
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

        <div v-else-if="selectedMethod === 'canvas'" class="flex-1 overflow-hidden">
          <SfgCanvas @yaml-change="panelYaml = $event" />
        </div>
      </section>
    </template>
  </div>

  <!-- YAML result slide-in panel -->
  <YamlResultPanel
    :is-open="panelOpen"
    :yaml-content="panelYaml"
    :workflow-name="panelName"
    @close="panelOpen = false"
    @open-in-canvas="openInCanvas"
    @save-as-draft="saveAsDraft"
  />
</template>
