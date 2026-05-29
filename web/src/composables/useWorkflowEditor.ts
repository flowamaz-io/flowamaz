import { ref, computed } from 'vue';
import { useDebounceFn } from '@vueuse/core';
import * as jsYaml from 'js-yaml';
import { workflowService } from '@/services/workflow.service';
import { creationService } from '@/services/creation.service';

export type SaveState = 'saved' | 'unsaved' | 'saving';

export function useWorkflowEditor(workspaceId: string, workflowId: string) {
  const yaml = ref('');
  const originalYaml = ref('');
  const healthScore = ref<number | null>(null);
  const saveState = ref<SaveState>('saved');
  const validationErrors = ref<Array<{ from: number; to: number; message: string; severity: 'error' | 'warning' }>>([]);
  const copilotHistory = ref<string[]>(loadHistory());
  const splitRatio = ref<number>(loadSplitRatio());

  const isDirty = computed(() => yaml.value !== originalYaml.value);

  async function loadWorkflow() {
    const wf = await workflowService.get(workspaceId, workflowId);
    yaml.value = wf.yamlContent ?? '';
    originalYaml.value = yaml.value;
    healthScore.value = wf.healthScore ?? null;
  }

  const validateDebounced = useDebounceFn(async () => {
    if (!yaml.value.trim()) { validationErrors.value = []; return; }
    try {
      const result = await workflowService.validate(workspaceId, yaml.value);
      validationErrors.value = [
        ...result.errors.map((e: { message: string; line?: number }) => ({
          from: lineToOffset(yaml.value, (e.line ?? 1) - 1),
          to: lineToOffset(yaml.value, e.line ?? 1),
          message: e.message,
          severity: 'error' as const,
        })),
        ...result.warnings.map((w: { message: string; line?: number }) => ({
          from: lineToOffset(yaml.value, (w.line ?? 1) - 1),
          to: lineToOffset(yaml.value, w.line ?? 1),
          message: w.message,
          severity: 'warning' as const,
        })),
      ];
    } catch {
      validationErrors.value = [];
    }
  }, 800);

  async function save() {
    if (!isDirty.value) return;
    saveState.value = 'saving';
    try {
      await workflowService.update(workspaceId, workflowId, { yamlContent: yaml.value });
      originalYaml.value = yaml.value;
      saveState.value = 'saved';
    } catch {
      saveState.value = 'unsaved';
    }
  }

  async function sendCopilotCommand(command: string): Promise<{ patch: string | null; pattern: string | null }> {
    const result = await creationService.sendCopilotCommand(workspaceId, workflowId, command, yaml.value);
    addToHistory(command);
    return { patch: result.yamlPatch, pattern: result.matchedPattern };
  }

  function applyPatch(patch: string): string | null {
    try {
      const patchDoc = jsYaml.load(patch) as Record<string, unknown> | null;
      if (!patchDoc?.spec) {
        return 'Could not apply — suggestion was not in the correct format. Try rephrasing your command.';
      }
      const patchSpec = patchDoc.spec as Record<string, unknown>;
      const patchNodes = (patchSpec.nodes as Array<Record<string, unknown>>) ?? [];
      const patchEdges = (patchSpec.edges as Array<Record<string, unknown>>) ?? [];
      if (!patchNodes.length && !patchEdges.length) {
        return 'No changes to apply in this suggestion.';
      }

      const current = jsYaml.load(yaml.value) as Record<string, unknown>;
      const currentSpec = (current?.spec ?? {}) as Record<string, unknown>;

      // Merge nodes — update existing by id, add new
      const currentNodes = (currentSpec.nodes as Array<Record<string, unknown>>) ?? [];
      if (patchNodes.length) {
        for (const node of patchNodes) {
          const existingIdx = currentNodes.findIndex(n => n.id === node.id);
          if (existingIdx >= 0) {
            currentNodes[existingIdx] = node;
          } else {
            currentNodes.push(node);
          }
        }
        currentSpec.nodes = currentNodes;
      }

      // Merge edges — update existing by id, add new
      const currentEdges = (currentSpec.edges as Array<Record<string, unknown>>) ?? [];
      if (patchEdges.length) {
        const existingEdgeIds = new Set(currentEdges.map(e => e.id));
        for (const edge of patchEdges) {
          if (existingEdgeIds.has(edge.id)) {
            const idx = currentEdges.findIndex(e => e.id === edge.id);
            currentEdges[idx] = edge;
          } else {
            currentEdges.push(edge);
          }
        }
        currentSpec.edges = currentEdges;
      }

      // Merge variables if present
      if (patchSpec.variables) {
        currentSpec.variables = { ...(currentSpec.variables as object ?? {}), ...(patchSpec.variables as object) };
      }

      current.spec = currentSpec;
      yaml.value = jsYaml.dump(current, { indent: 2, lineWidth: -1, quotingType: '"', forceQuotes: false });
      return null;
    } catch {
      return 'Could not apply — invalid YAML patch.';
    }
  }

  function updateSplitRatio(ratio: number) {
    splitRatio.value = ratio;
    localStorage.setItem('fm:editor:split', String(ratio));
  }

  function addToHistory(command: string) {
    copilotHistory.value = [command, ...copilotHistory.value.filter(c => c !== command)].slice(0, 10);
    localStorage.setItem('fm:copilot:history', JSON.stringify(copilotHistory.value));
  }

  return {
    yaml, healthScore, saveState, isDirty, validationErrors, copilotHistory, splitRatio,
    loadWorkflow, validateDebounced, save, sendCopilotCommand, applyPatch, updateSplitRatio,
  };
}

function loadHistory(): string[] {
  try { return JSON.parse(localStorage.getItem('fm:copilot:history') ?? '[]'); }
  catch { return []; }
}

function loadSplitRatio(): number {
  const stored = localStorage.getItem('fm:editor:split');
  return stored ? parseFloat(stored) : 60;
}

function lineToOffset(text: string, line: number): number {
  const lines = text.split('\n');
  return lines.slice(0, line).reduce((acc, l) => acc + l.length + 1, 0);
}
