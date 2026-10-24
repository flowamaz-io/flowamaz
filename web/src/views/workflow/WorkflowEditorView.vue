<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue';
import { useRoute, onBeforeRouteLeave } from 'vue-router';
import { onClickOutside } from '@vueuse/core';
import { XCircle, AlertTriangle, CheckCircle2 } from 'lucide-vue-next';
import FmModal from '../../components/common/FmModal.vue';
import SfgCanvas from '../../components/canvas/SfgCanvas.vue';
import FmYamlEditor from '../../components/editor/FmYamlEditor.vue';
import CopilotPanel from '../../components/editor/CopilotPanel.vue';
import EmpathyPanel from '../../components/editor/EmpathyPanel.vue';
import { useWorkflowEditor } from '../../composables/useWorkflowEditor';
import { useHelp } from '../../composables/useHelp';
import { useWorkspace } from '../../composables/useWorkspace';
import { useWorkflowStore } from '../../stores/workflow.store';
import { useToast } from '../../composables/useToast';
import { useCanvasStore } from '../../stores/canvas.store';
import { findNodeIdLine } from '../../utils/canvas.util';
import { copilotErrorMessage } from '../../utils/error.util';

const route = useRoute();
const ws = useWorkspace();
const workspaceId = (route.query.workspaceId as string) || ws.currentWorkspaceId.value || '';
const workflowId = route.params.id as string;
const workflowStore = useWorkflowStore();

const {
  yaml, healthScore, saveState, isDirty, validationErrors,
  copilotHistory, splitRatio,
  loadWorkflow, validateDebounced, validate, save: saveWorkflow, sendCopilotCommand, applyPatch, updateSplitRatio,
} = useWorkflowEditor(workspaceId, workflowId);

const toast = useToast();

/** Save, telling the user what to do when it fails (the composable only reports success). */
async function save(): Promise<boolean> {
  const ok = await saveWorkflow();
  if (!ok) {
    toast.error("Couldn't save the workflow — your changes are still here. Check your connection and press Save again.");
  }
  return ok;
}

// ── Unsaved-changes guard ────────────────────────────────────────────────────
// Leaving the editor (in-app navigation) asks first; closing/reloading the tab gets the
// browser's own prompt, which is the only thing browsers allow during unload.
const leaveDialogOpen = ref(false);
const leaveSaving = ref(false);
let resolveLeave: ((proceed: boolean) => void) | null = null;

function askToLeave(): Promise<boolean> {
  leaveDialogOpen.value = true;
  return new Promise((resolve) => { resolveLeave = resolve; });
}

function finishLeave(proceed: boolean): void {
  leaveDialogOpen.value = false;
  const resolve = resolveLeave;
  resolveLeave = null;
  resolve?.(proceed);
}

async function saveAndLeave(): Promise<void> {
  leaveSaving.value = true;
  try {
    // On failure stay on the page: the error toast explains what to do and nothing is lost.
    finishLeave(await save());
  } finally {
    leaveSaving.value = false;
  }
}

function onLeaveDialogToggle(open: boolean): void {
  // Closing the dialog (Esc, ×, backdrop) means "Stay".
  if (!open) finishLeave(false);
}

onBeforeRouteLeave(async () => {
  if (!isDirty.value) return true;
  return askToLeave();
});

function onBeforeUnload(e: BeforeUnloadEvent): void {
  if (!isDirty.value) return;
  e.preventDefault();
  // Legacy browsers need returnValue set to show the prompt.
  e.returnValue = '';
}

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
    // Show the server's actionable message (e.g. a rejected AI key) verbatim; only a real network
    // failure mentions the connection.
    copilotPanelRef.value?.setResult(null, null, copilotErrorMessage(e));
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

// ── Canvas → YAML jump ("Show in YAML" / node selection) ─────────────────────
const canvasStore = useCanvasStore();
const yamlEditorRef = ref<InstanceType<typeof FmYamlEditor> | null>(null);

watch(() => canvasStore.yamlRevealRequest, async (req) => {
  if (!req) return;
  if (empathyMode.value) {
    // Passive selection shouldn't yank the user out of Empathy mode; an explicit request does.
    if (!req.explicit) return;
    empathyMode.value = false;
  }
  // Let the latest canvas edit flow into the editor document before locating the line.
  await nextTick();
  const line = findNodeIdLine(yaml.value, req.nodeId);
  if (line === null) {
    yamlEditorRef.value?.clearRevealedLine();
    if (req.explicit) {
      toast.warning(`Node "${req.nodeId}" isn't in the YAML yet. Make a change on the canvas or save, then try "Show in YAML" again.`);
    }
    return;
  }
  yamlEditorRef.value?.revealLine(line, { focus: req.explicit });
});

watch(yaml, () => validateDebounced());
watch(saveState, (next, prev) => {
  if (next === 'saved' && prev === 'saving') workflowStore.bumpYamlVersion();
});

onMounted(async () => {
  window.addEventListener('keydown', onKeydown);
  window.addEventListener('mousemove', onMousemove);
  window.addEventListener('mouseup', onMouseup);
  window.addEventListener('beforeunload', onBeforeUnload);
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
  window.removeEventListener('beforeunload', onBeforeUnload);
  // A pending leave prompt must not keep the navigation hanging.
  finishLeave(false);
});
</script>

<template>
  <div class="flex flex-col h-full overflow-hidden bg-neutral-950">
    <!-- Loading -->
    <div
      v-if="loading"
      class="flex-1 flex items-center justify-center"
    >
      <div class="w-8 h-8 border-4 border-violet-500 border-t-transparent rounded-full animate-spin" />
    </div>

    <!-- Error -->
    <div
      v-else-if="error"
      class="flex-1 flex items-center justify-center text-red-400 text-sm p-8"
    >
      {{ error }}
    </div>

    <template v-else>
      <!-- Toolbar -->
      <div class="flex items-center gap-3 px-4 py-2 bg-neutral-900 border-b border-neutral-700 shrink-0">
        <span class="text-sm font-semibold text-white truncate flex-1">Edit workflow</span>

        <!-- Health score -->
        <div
          v-if="healthScore !== null"
          :class="['text-xs font-medium', healthColour(healthScore)]"
        >
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
        >
          Save
        </button>

        <button
          type="button"
          class="text-xs bg-violet-900 hover:bg-violet-800 text-violet-300 rounded-lg px-3 py-1.5"
          data-test="copilot-toggle"
          :aria-expanded="copilotOpen"
          @click="copilotOpen = !copilotOpen"
        >
          ✦ Co-pilot <kbd class="ml-1 text-neutral-400">⌘K</kbd>
        </button>

        <!-- Validation badge -->
        <div
          ref="validationBadgeRef"
          class="relative shrink-0"
        >
          <!-- Fixed minimum width: the label changes when async validation lands ("Valid" →
               "2 errors"); without it every toolbar button to the left shifted under the cursor
               and a click aimed at Co-pilot could land beside it. -->
          <button
            class="flex min-w-[8.5rem] items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors"
            :class="validationErrorCount > 0
              ? 'bg-red-100 text-red-700 hover:bg-red-200'
              : validationWarningCount > 0
                ? 'bg-amber-100 text-amber-700 hover:bg-amber-200'
                : 'bg-gray-100 text-gray-600 hover:bg-gray-200'"
            @click="onValidationBadgeClick"
          >
            <XCircle
              v-if="validationErrorCount > 0"
              class="w-3.5 h-3.5"
            />
            <AlertTriangle
              v-else-if="validationWarningCount > 0"
              class="w-3.5 h-3.5"
            />
            <CheckCircle2
              v-else
              class="w-3.5 h-3.5 text-teal-500"
            />
            <span v-if="validationErrorCount > 0">{{ validationErrorCount }} error{{ validationErrorCount > 1 ? 's' : '' }}</span>
            <span v-else-if="validationWarningCount > 0">{{ validationWarningCount }} warning{{ validationWarningCount > 1 ? 's' : '' }}</span>
            <span v-else>Valid</span>
          </button>

          <div
            v-if="showValidationPanel"
            class="absolute top-full mt-1 right-0 w-96 bg-white rounded-lg shadow-xl border border-gray-200 z-50 p-3"
          >
            <div
              v-if="validationErrorsList.length > 0"
              class="mb-3"
            >
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
          >
            Technical
          </button>
          <button
            :class="['text-xs px-3 py-1.5 transition-colors', empathyMode ? 'bg-teal-600 text-white' : 'bg-gray-100 text-gray-700 hover:text-gray-900']"
            @click="enableEmpathy"
          >
            Empathy
          </button>
        </div>

        <button
          class="text-xs text-neutral-400 hover:text-white"
          @click="openArticle('workflow-empathy')"
        >
          Help
        </button>
      </div>

      <!-- Editor split pane -->
      <div
        class="flex flex-1 overflow-hidden"
        :class="{ 'cursor-col-resize': dragging }"
      >
        <!-- Canvas -->
        <div
          :style="{ width: splitRatio + '%' }"
          class="relative overflow-hidden border-r border-neutral-700"
        >
          <SfgCanvas
            :key="workflowId"
            :initial-yaml="yaml"
            :saving="saveState === 'saving'"
            :is-dirty="isDirty"
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

    <!-- Unsaved-changes prompt when leaving the editor -->
    <FmModal
      :model-value="leaveDialogOpen"
      size="sm"
      title="Unsaved changes"
      @update:model-value="onLeaveDialogToggle"
    >
      <div
        class="flex gap-3"
        data-test="unsaved-changes-dialog"
      >
        <span class="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-amber-100 text-amber-600">
          <AlertTriangle class="h-5 w-5" />
        </span>
        <p class="text-sm text-slate-700">
          This workflow has changes that haven't been saved. Save them before you leave, or discard
          them — discarded changes can't be recovered.
        </p>
      </div>
      <template #footer>
        <button
          type="button"
          data-test="leave-stay"
          :disabled="leaveSaving"
          class="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
          @click="finishLeave(false)"
        >
          Stay
        </button>
        <button
          type="button"
          data-test="leave-discard"
          :disabled="leaveSaving"
          class="rounded-lg border border-red-300 px-4 py-2 text-sm font-medium text-red-700 hover:bg-red-50 disabled:opacity-50"
          @click="finishLeave(true)"
        >
          Discard changes
        </button>
        <button
          type="button"
          data-test="leave-save"
          :disabled="leaveSaving"
          class="rounded-lg bg-violet-600 px-4 py-2 text-sm font-medium text-white hover:bg-violet-500 disabled:opacity-50"
          @click="saveAndLeave"
        >
          {{ leaveSaving ? 'Saving…' : 'Save and leave' }}
        </button>
      </template>
    </FmModal>
  </div>
</template>
