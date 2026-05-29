<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import { useRoute } from 'vue-router';
import { onClickOutside } from '@vueuse/core';
import { XCircle, AlertTriangle, CheckCircle2 } from 'lucide-vue-next';
import SfgCanvas from '../../components/canvas/SfgCanvas.vue';
import FmYamlEditor from '../../components/editor/FmYamlEditor.vue';
import CopilotPanel from '../../components/editor/CopilotPanel.vue';
import EmpathyPanel from '../../components/editor/EmpathyPanel.vue';
import { useWorkflowEditor } from '../../composables/useWorkflowEditor';
import { useHelp } from '../../composables/useHelp';
import { useWorkspace } from '../../composables/useWorkspace';
import { useWorkflowStore } from '../../stores/workflow.store';
import { useToast } from '../../composables/useToast';

const route = useRoute();
const ws = useWorkspace();
const workspaceId = (route.query.workspaceId as string) || ws.currentWorkspaceId.value || '';
const workflowId = route.params.id as string;
const workflowStore = useWorkflowStore();

const {
  yaml, healthScore, saveState, isDirty, validationErrors,
  copilotHistory, splitRatio,
  loadWorkflow, validateDebounced, validate, save, sendCopilotCommand, applyPatch, updateSplitRatio,
} = useWorkflowEditor(workspaceId, workflowId);

const toast = useToast();

const { openArticle } = useHelp();

const validationErrorsList = computed(() => validationErrors.value.filter(e => e.severity === 'error'));
const validationWarningsList = computed(() => validationErrors.value.filter(e => e.severity === 'warning'));
const validationErrorCount = computed(() => validationErrorsList.value.length);
const validationWarningCount = computed(() => validationWarningsList.value.length);

const showValidationPanel = ref(false);
const validationBadgeRef = ref<HTMLDivElement | null>(null);
onClickOutside(validationBadgeRef, () => { showValidationPanel.value = false; });

const copilotOpen = ref(false);
const copilotPanelRef = ref<InstanceType<typeof CopilotPanel> | null>(null);
const empathyMode = ref(false);

const loading = ref(true);
const error = ref<string | null>(null);

// Split pane drag
const dragging = ref(false);
function onDividerMousedown() {
  dragging.value = true;
}
function onMousemove(e: MouseEvent) {
  if (!dragging.value) return;
  const totalWidth = window.innerWidth;
  const ratio = Math.max(20, Math.min(80, (e.clientX / totalWidth) * 100));
  updateSplitRatio(ratio);
}
function onMouseup() { dragging.value = false; }

// Keyboard shortcuts
function onKeydown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
    e.preventDefault();
    copilotOpen.value = !copilotOpen.value;
  }
  if ((e.ctrlKey || e.metaKey) && e.key === 's') {
    e.preventDefault();
    save();
  }
}

async function enableEmpathy() {
  if (isDirty.value) await save();
  empathyMode.value = true;
}

// Co-pilot command flow
async function onCopilotCommand(cmd: string) {
  if (cmd.startsWith('__apply__')) {
    const applyErr = applyPatch(cmd.slice(9));
    if (applyErr) copilotPanelRef.value?.setResult(null, null, applyErr);
    return;
  }
  if (isDirty.value) await save();
  try {
    const { patch, pattern } = await sendCopilotCommand(cmd);
    copilotPanelRef.value?.setResult(patch, pattern);
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : 'Request failed';
    copilotPanelRef.value?.setResult(null, null, `Co-pilot request failed: ${msg}. Check your connection and try again.`);
  }
}

async function onValidationBadgeClick() {
  const hasIssues = validationErrorCount.value > 0 || validationWarningCount.value > 0;
  if (!hasIssues) {
    // Re-validate and show toast result
    const result = await validate();
    if (!result) return;
    if (result.errors.length === 0 && result.warnings.length === 0) {
      toast.success('Workflow is valid — ready to publish');
    } else {
      showValidationPanel.value = true;
    }
    return;
  }
  showValidationPanel.value = !showValidationPanel.value;
}


// Health score badge colour
function healthColour(score: number | null) {
  if (score === null) return 'text-neutral-400';
  if (score >= 80) return 'text-emerald-400';
  if (score >= 50) return 'text-amber-400';
  return 'text-red-400';
}

watch(yaml, () => validateDebounced());
watch(saveState, (next, prev) => {
  if (next === 'saved' && prev === 'saving') workflowStore.bumpYamlVersion();
});

onMounted(async () => {
  window.addEventListener('keydown', onKeydown);
  window.addEventListener('mousemove', onMousemove);
  window.addEventListener('mouseup', onMouseup);
  try {
    await loadWorkflow();
  } catch (e: unknown) {
    error.value = `Failed to load workflow. ${e instanceof Error ? e.message : 'Please refresh and try again.'}`;
  } finally {
    loading.value = false;
  }
});

onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown);
  window.removeEventListener('mousemove', onMousemove);
  window.removeEventListener('mouseup', onMouseup);
});
</script>

<template>
  <div class="flex flex-col h-full overflow-hidden bg-neutral-950">
    <!-- Loading -->
    <div v-if="loading" class="flex-1 flex items-center justify-center">
      <div class="w-8 h-8 border-4 border-violet-500 border-t-transparent rounded-full animate-spin" />
    </div>

    <!-- Error -->
    <div v-else-if="error" class="flex-1 flex items-center justify-center text-red-400 text-sm p-8">{{ error }}</div>

    <template v-else>
      <!-- Toolbar -->
      <div class="flex items-center gap-3 px-4 py-2 bg-neutral-900 border-b border-neutral-700 shrink-0">
        <span class="text-sm font-semibold text-white truncate flex-1">Edit workflow</span>

        <!-- Health score -->
        <div v-if="healthScore !== null" :class="['text-xs font-medium', healthColour(healthScore)]">
          Health: {{ healthScore }}%
        </div>

        <!-- Save state -->
        <span class="text-xs text-neutral-400">
          <template v-if="saveState === 'saving'">Saving…</template>
          <template v-else-if="saveState === 'saved' && !isDirty">Saved</template>
          <template v-else-if="isDirty">Unsaved changes</template>
        </span>

        <button
          class="text-xs bg-violet-600 hover:bg-violet-500 text-white rounded-lg px-3 py-1.5 disabled:opacity-40"
          :disabled="!isDirty || saveState === 'saving'"
          @click="save"
        >Save</button>

        <button
          class="text-xs bg-violet-900 hover:bg-violet-800 text-violet-300 rounded-lg px-3 py-1.5"
          @click="copilotOpen = !copilotOpen"
        >✦ Co-pilot <kbd class="ml-1 text-neutral-400">⌘K</kbd></button>

        <!-- Validation badge -->
        <div ref="validationBadgeRef" class="relative shrink-0">
          <button
            class="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors"
            :class="validationErrorCount > 0
              ? 'bg-red-100 text-red-700 hover:bg-red-200'
              : validationWarningCount > 0
                ? 'bg-amber-100 text-amber-700 hover:bg-amber-200'
                : 'bg-gray-100 text-gray-600 hover:bg-gray-200'"
            @click="onValidationBadgeClick"
          >
            <XCircle v-if="validationErrorCount > 0" class="w-3.5 h-3.5" />
            <AlertTriangle v-else-if="validationWarningCount > 0" class="w-3.5 h-3.5" />
            <CheckCircle2 v-else class="w-3.5 h-3.5 text-teal-500" />
            <span v-if="validationErrorCount > 0">{{ validationErrorCount }} error{{ validationErrorCount > 1 ? 's' : '' }}</span>
            <span v-else-if="validationWarningCount > 0">{{ validationWarningCount }} warning{{ validationWarningCount > 1 ? 's' : '' }}</span>
            <span v-else>Valid</span>
          </button>

          <div
            v-if="showValidationPanel"
            class="absolute top-full mt-1 right-0 w-96 bg-white rounded-lg shadow-xl border border-gray-200 z-50 p-3"
          >
            <div v-if="validationErrorsList.length > 0" class="mb-3">
              <p class="text-xs font-semibold text-red-700 uppercase tracking-wider mb-1">
                Errors — must fix before publishing
              </p>
              <ul class="space-y-1">
                <li
                  v-for="err in validationErrorsList"
                  :key="err.message"
                  class="text-xs text-red-700 flex gap-2"
                >
                  <XCircle class="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                  {{ err.message }}
                </li>
              </ul>
            </div>
            <div v-if="validationWarningsList.length > 0">
              <p class="text-xs font-semibold text-amber-700 uppercase tracking-wider mb-1">
                Warnings
              </p>
              <ul class="space-y-1">
                <li
                  v-for="warn in validationWarningsList"
                  :key="warn.message"
                  class="text-xs text-amber-700 flex gap-2"
                >
                  <AlertTriangle class="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                  {{ warn.message }}
                </li>
              </ul>
            </div>
          </div>
        </div>

        <!-- Mode toggle -->
        <div class="flex rounded-lg overflow-hidden border border-gray-300 shrink-0">
          <button
            :class="['text-xs px-3 py-1.5 transition-colors', !empathyMode ? 'bg-teal-600 text-white' : 'bg-gray-100 text-gray-700 hover:text-gray-900']"
            @click="empathyMode = false"
          >Technical</button>
          <button
            :class="['text-xs px-3 py-1.5 transition-colors', empathyMode ? 'bg-teal-600 text-white' : 'bg-gray-100 text-gray-700 hover:text-gray-900']"
            @click="enableEmpathy"
          >Empathy</button>
        </div>

        <button
          class="text-xs text-neutral-400 hover:text-white"
          @click="openArticle('workflow-empathy')"
        >Help</button>
      </div>

      <!-- Editor split pane -->
      <div class="flex flex-1 overflow-hidden" :class="{ 'cursor-col-resize': dragging }">
        <!-- Canvas -->
        <div :style="{ width: splitRatio + '%' }" class="relative overflow-hidden border-r border-neutral-700">
          <SfgCanvas
            :key="workflowId"
            :initial-yaml="yaml"
            :saving="saveState === 'saving'"
            @yaml-change="yaml = $event"
            @save="save"
          />
        </div>

        <!-- Divider -->
        <div
          class="w-1 bg-neutral-700 hover:bg-violet-600 cursor-col-resize transition-colors shrink-0"
          @mousedown.prevent="onDividerMousedown"
        />

        <!-- Right panel: YAML editor (technical) or Empathy panel -->
        <div class="flex-1 overflow-hidden bg-white">
          <EmpathyPanel
            v-if="empathyMode"
            :workspace-id="workspaceId"
            :workflow-id="workflowId"
            :visible="empathyMode"
            :yaml-content="yaml"
          />
          <FmYamlEditor
            v-else
            ref="yamlEditorRef"
            v-model="yaml"
            :diagnostics="validationErrors"
            :dark-mode="false"
          />
        </div>
      </div>

      <!-- Co-pilot panel -->
      <CopilotPanel
        ref="copilotPanelRef"
        :open="copilotOpen"
        :history="copilotHistory"
        @close="copilotOpen = false"
        @command="onCopilotCommand"
      />
    </template>
  </div>
</template>
