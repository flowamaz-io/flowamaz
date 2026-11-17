<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref } from 'vue';
import FmBadge from './FmBadge.vue';
import FmButton from './FmButton.vue';
import FmErrorState from './FmErrorState.vue';
import FmSpinner from './FmSpinner.vue';
import FmYamlEditor from './FmYamlEditor.vue';
import WorkflowGraphPreview from './WorkflowGraphPreview.vue';
import { describeError } from '../api';
import { useWorkbenchApi } from '../host';
import type { WorkflowDefinitionResponse } from '../types';

// Edit one workflow on the WorkbenchApi alone: its YAML on the left and, on the right, either a live picture of it or
// (with `canvas`) the visual editor, kept in step with the YAML. Save and Publish (which saves first); the server
// validates on publish and its message is shown as it is.
const props = withDefaults(defineProps<{ workflowId: string; readOnly?: boolean; canvas?: boolean }>(), { readOnly: false, canvas: false });

// Loaded only when used: the canvas brings in Cytoscape.
const SfgCanvas = defineAsyncComponent(() => import('./canvas/SfgCanvas.vue'));
const emit = defineEmits<{ published: [workflow: WorkflowDefinitionResponse]; back: [] }>();

const api = useWorkbenchApi();
const workflow = ref<WorkflowDefinitionResponse | null>(null);
const yaml = ref('');
const savedYaml = ref('');
const loadError = ref<{ message: string; code?: string; status?: number } | null>(null);
const problem = ref<string | null>(null);
const notice = ref<string | null>(null);
const drawable = ref(true);
const busy = ref<'save' | 'publish' | null>(null);

const dirty = computed(() => yaml.value !== savedYaml.value);

async function load(): Promise<void> {
  loadError.value = null;
  try {
    workflow.value = await api.getWorkflow(props.workflowId);
    yaml.value = workflow.value.yamlContent;
    savedYaml.value = workflow.value.yamlContent;
  } catch (e) {
    loadError.value = describeError(e);
  }
}

async function save(): Promise<boolean> {
  if (props.readOnly || !dirty.value) return true;
  busy.value = 'save';
  problem.value = null;
  notice.value = null;
  try {
    const text = yaml.value;
    workflow.value = await api.saveWorkflow(props.workflowId, text);
    savedYaml.value = text;
    notice.value = 'Saved.';
    return true;
  } catch (e) {
    problem.value = `Not saved: ${describeError(e).message} Your changes are still here.`;
    return false;
  } finally {
    busy.value = null;
  }
}

async function publish(): Promise<void> {
  if (!(await save())) return;
  busy.value = 'publish';
  problem.value = null;
  notice.value = null;
  try {
    const version = await api.publishWorkflow(props.workflowId);
    workflow.value = await api.getWorkflow(props.workflowId);
    notice.value = `Published${version.tagName ? ` as ${version.tagName}` : ''}. New runs use this version.`;
    emit('published', workflow.value);
  } catch (e) {
    problem.value = `Not published: ${describeError(e).message}`;
  } finally {
    busy.value = null;
  }
}

function onKeydown(e: KeyboardEvent): void {
  if ((e.ctrlKey || e.metaKey) && e.key === 's') {
    e.preventDefault();
    void save();
  }
}

function onBeforeUnload(e: BeforeUnloadEvent): void {
  if (!dirty.value) return;
  e.preventDefault();
  e.returnValue = '';
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown);
  window.addEventListener('beforeunload', onBeforeUnload);
  void load();
});
onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown);
  window.removeEventListener('beforeunload', onBeforeUnload);
});

defineExpose({ dirty, save });
</script>

<template>
  <div class="flex h-full flex-col">
    <FmErrorState
      v-if="loadError"
      :title="loadError.status === 404 ? 'Workflow not found' : 'Couldn\'t open this workflow'"
      :description="loadError.message"
      :status-code="loadError.status"
      :error-code="loadError.code"
      :action-label="loadError.status === 404 ? 'Back to workflows' : 'Try again'"
      @action="loadError.status === 404 ? emit('back') : load()"
    />
    <div
      v-else-if="!workflow"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>
    <template v-else>
      <div class="flex items-center justify-between gap-4 border-b border-slate-200 bg-white px-4 py-3">
        <div class="flex min-w-0 items-center gap-3">
          <button
            type="button"
            class="text-sm text-slate-400 hover:text-slate-600"
            @click="emit('back')"
          >
            ←
          </button>
          <h1 class="truncate text-base font-semibold text-slate-900">
            {{ workflow.name }}
          </h1>
          <FmBadge :variant="workflow.status === 'Published' ? 'primary' : 'slate'">
            {{ workflow.status }}
          </FmBadge>
          <span
            v-if="dirty"
            class="text-xs text-amber-600"
            data-testid="editor-unsaved"
          >Unsaved changes</span>
        </div>
        <div
          v-if="!readOnly"
          class="flex gap-2"
        >
          <FmButton
            variant="secondary"
            :disabled="!dirty || busy !== null"
            :loading="busy === 'save'"
            data-testid="editor-save"
            @click="save"
          >
            Save
          </FmButton>
          <FmButton
            :disabled="busy !== null || workflow.status === 'Archived'"
            :loading="busy === 'publish'"
            data-testid="editor-publish"
            @click="publish"
          >
            Publish
          </FmButton>
        </div>
      </div>

      <p
        v-if="problem"
        role="alert"
        class="border-b border-danger-200 bg-danger-50 px-4 py-2 text-sm text-danger-700"
      >
        {{ problem }}
      </p>
      <p
        v-else-if="notice"
        role="status"
        class="border-b border-primary-200 bg-primary-50 px-4 py-2 text-sm text-primary-700"
      >
        {{ notice }}
      </p>
      <p
        v-if="workflow.status === 'Archived' && !readOnly"
        class="border-b border-slate-200 bg-slate-50 px-4 py-2 text-sm text-slate-600"
      >
        This workflow is archived. Unarchive it from the workflow list before publishing a new version.
      </p>

      <div class="grid min-h-0 flex-1 grid-cols-1 lg:grid-cols-2">
        <div class="min-h-0 overflow-auto border-r border-slate-200">
          <FmYamlEditor
            v-model="yaml"
            :read-only="readOnly"
          />
        </div>
        <div
          v-if="canvas"
          class="relative h-[32rem] min-h-0 lg:h-auto"
          data-testid="editor-canvas"
        >
          <SfgCanvas
            :workflow-id="workflowId"
            :initial-yaml="yaml"
            :read-only="readOnly"
            :saving="busy === 'save'"
            :is-dirty="dirty"
            @yaml-change="yaml = $event"
            @save="save"
          />
        </div>
        <div
          v-else
          class="relative min-h-64"
        >
          <WorkflowGraphPreview
            :yaml="yaml"
            @valid="(ok: boolean) => (drawable = ok)"
          />
          <p
            v-if="!drawable"
            class="absolute inset-x-4 top-4 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800"
            data-testid="preview-invalid"
          >
            The YAML can't be drawn as a workflow right now (check the indentation and the nodes / edges lists).
            The picture shows the last version that could be.
          </p>
        </div>
      </div>
    </template>
  </div>
</template>
