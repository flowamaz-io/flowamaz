<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue';
import { useRoute } from 'vue-router';
import SfgCanvas from '../../components/canvas/SfgCanvas.vue';
import FmYamlEditor from '../../components/editor/FmYamlEditor.vue';
import CopilotPanel from '../../components/editor/CopilotPanel.vue';
import EmpathyPanel from '../../components/editor/EmpathyPanel.vue';
import { useWorkflowEditor } from '../../composables/useWorkflowEditor';
import { useHelp } from '../../composables/useHelp';
import { useWorkspace } from '../../composables/useWorkspace';

const route = useRoute();
const ws = useWorkspace();
const workspaceId = (route.query.workspaceId as string) || ws.currentWorkspaceId.value || '';
const workflowId = route.params.id as string;

const {
  yaml, healthScore, saveState, isDirty, validationErrors,
  copilotHistory, splitRatio,
  loadWorkflow, validateDebounced, save, sendCopilotCommand, applyPatch, updateSplitRatio,
} = useWorkflowEditor(workspaceId, workflowId);

const { openArticle } = useHelp();

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

// Co-pilot command flow
async function onCopilotCommand(cmd: string) {
  if (cmd.startsWith('__apply__')) {
    applyPatch(cmd.slice(9));
    return;
  }
  const { patch, pattern } = await sendCopilotCommand(cmd);
  copilotPanelRef.value?.setResult(patch, pattern);
}

// Health score badge colour
function healthColour(score: number | null) {
  if (score === null) return 'text-neutral-400';
  if (score >= 80) return 'text-emerald-400';
  if (score >= 50) return 'text-amber-400';
  return 'text-red-400';
}

watch(yaml, () => validateDebounced());

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
          class="text-xs bg-neutral-700 hover:bg-neutral-600 text-white rounded-lg px-3 py-1.5"
          @click="validateDebounced()"
        >Validate</button>

        <button
          class="text-xs bg-violet-900 hover:bg-violet-800 text-violet-300 rounded-lg px-3 py-1.5"
          @click="copilotOpen = !copilotOpen"
        >✦ Co-pilot <kbd class="ml-1 text-neutral-400">⌘K</kbd></button>

        <!-- Mode toggle -->
        <div class="flex rounded-lg overflow-hidden border border-neutral-600 shrink-0">
          <button
            :class="['text-xs px-3 py-1.5 transition-colors', !empathyMode ? 'bg-violet-600 text-white' : 'bg-neutral-800 text-neutral-400 hover:text-white']"
            @click="empathyMode = false"
          >Technical</button>
          <button
            :class="['text-xs px-3 py-1.5 transition-colors', empathyMode ? 'bg-violet-600 text-white' : 'bg-neutral-800 text-neutral-400 hover:text-white']"
            @click="empathyMode = true"
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
            :initial-yaml="yaml"
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
        <div class="flex-1 overflow-hidden">
          <EmpathyPanel
            v-if="empathyMode"
            :workspace-id="workspaceId"
            :workflow-id="workflowId"
            :visible="empathyMode"
          />
          <FmYamlEditor
            v-else
            ref="yamlEditorRef"
            v-model="yaml"
            :diagnostics="validationErrors"
            :dark-mode="true"
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
