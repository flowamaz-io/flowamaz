import { ref, computed } from 'vue';
import { useDebounceFn } from '@vueuse/core';
import * as jsYaml from 'js-yaml';
import { workflowService } from '@/services/workflow.service';
import { creationService } from '@/services/creation.service';
import { dumpWorkflowYaml, expandYamlAliases } from '@/utils/yaml.util';

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
    // Expand anchors/aliases (e.g. a server skeleton's `nodes: &o0 []` / `edges: *o0`) so the
    // editor shows plain YAML and nodes/edges never share one array.
    yaml.value = expandYamlAliases(wf.yamlContent ?? '');
    originalYaml.value = yaml.value;
    healthScore.value = wf.healthScore ?? null;
  }

  type Issue = { message: string; line?: number };

  /** Turn a validation result for `text` into editor markers (line → character offsets). */
  function toMarkers(text: string, result: { errors: Issue[]; warnings: Issue[] }) {
    return [
      ...result.errors.map((e) => ({
        from: lineToOffset(text, (e.line ?? 1) - 1),
        to: lineToOffset(text, e.line ?? 1),
        message: e.message,
        severity: 'error' as const,
      })),
      ...result.warnings.map((w) => ({
        from: lineToOffset(text, (w.line ?? 1) - 1),
        to: lineToOffset(text, w.line ?? 1),
        message: w.message,
        severity: 'warning' as const,
      })),
    ];
  }

  /**
   * Validate the current document. A response for a document that has since changed (e.g.
   * nodes deleted on the canvas while the request was in flight) is dropped instead of
   * painting markers at stale positions; the newer document has its own validation queued.
   */
  async function runValidation(): Promise<{ errors: Issue[]; warnings: Issue[] } | null> {
    const text = yaml.value;
    if (!text.trim()) { validationErrors.value = []; return null; }
    try {
      const result = await workflowService.validate(workspaceId, text);
      if (yaml.value !== text) return null;
      validationErrors.value = toMarkers(text, result);
      return result;
    } catch {
      if (yaml.value === text) validationErrors.value = [];
      return null;
    }
  }

  const validateDebounced = useDebounceFn(async () => { await runValidation(); }, 800);

  /** Save the document. Resolves true when it was saved (or there was nothing to save). */
  async function save(): Promise<boolean> {
    if (!isDirty.value) return true;
    saveState.value = 'saving';
    const text = yaml.value;
    try {
      await workflowService.update(workspaceId, workflowId, { yamlContent: text });
      originalYaml.value = text;
      saveState.value = 'saved';
      return true;
    } catch {
      saveState.value = 'unsaved';
      return false;
    }
  }

  async function sendCopilotCommand(command: string): Promise<{ patch: string | null; pattern: string | null }> {
    const result = await creationService.sendCopilotCommand(workspaceId, workflowId, command, yaml.value);
    addToHistory(command);
    return { patch: result.yamlPatch, pattern: result.matchedPattern };
  }

  async function validate(): Promise<{ errors: Issue[]; warnings: Issue[] } | null> {
    return runValidation();
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
      validationErrors.value = []; // clear stale markers before patch lands
      yaml.value = dumpWorkflowYaml(current, { quotingType: '"', forceQuotes: false });
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
    loadWorkflow, validateDebounced, validate, save, sendCopilotCommand, applyPatch, updateSplitRatio,
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
