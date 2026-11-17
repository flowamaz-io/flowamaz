<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import {
  X, Zap, Play, Brain, UserCheck, GitBranch, CircleStop, Settings,
} from 'lucide-vue-next';
import TriggerConfig from './config/TriggerConfig.vue';
import ActionConfig from './config/ActionConfig.vue';
import HumanGateConfig from './config/HumanGateConfig.vue';
import RouterConditionBuilder from './config/RouterConditionBuilder.vue';
import AiNodeConfig from './config/AiNodeConfig.vue';
import EndConfig from './config/EndConfig.vue';
import InputMappingEditor, { type Mapping } from './shared/InputMappingEditor.vue';
import type { WorkflowVariable } from './shared/VariableAutocomplete.vue';
import type { CompensationBlock, RetryPolicy, TimeoutPolicy } from '@flowamaz/canvas/types';

export interface ConfigNode {
  id: string;
  type: string;
  label: string;
  config: Record<string, unknown>;
  /** The node-level retry block (YAML `retry:`), null when the node has none. */
  retry?: RetryPolicy | null;
  /** The node-level timeout block (YAML `timeout: { seconds, on_timeout }`). */
  timeout?: TimeoutPolicy | null;
  /** The node-level compensation block (YAML `compensate: { strategy, node_id }`). */
  compensate?: CompensationBlock | null;
}

/** What the panel saves. retry / timeout / compensate: null removes the block. */
export interface NodeConfigSave {
  id: string;
  label: string;
  config: Record<string, unknown>;
  retry?: RetryPolicy | null;
  timeout?: TimeoutPolicy | null;
  compensate?: CompensationBlock | null;
}

const props = withDefaults(
  defineProps<{
    open: boolean;
    node: ConfigNode | null;
    variables?: WorkflowVariable[];
    webhookUrl?: string;
    /** spec.trigger.type of the workflow, shown on trigger nodes that don't set their own. */
    workflowTriggerType?: string;
    /** Other steps (name + id), offered for "on timeout, go to" and the undo step. */
    nodeOptions?: Array<{ id: string; label: string }>;
  }>(),
  { variables: () => [], webhookUrl: '', workflowTriggerType: '', nodeOptions: () => [] },
);

const emit = defineEmits<{
  close: [];
  save: [payload: NodeConfigSave];
}>();

type Tab = 'Basic' | 'Inputs' | 'Outputs' | 'Advanced';
const TABS: Tab[] = ['Basic', 'Inputs', 'Outputs', 'Advanced'];
const activeTab = ref<Tab>('Basic');

const ICONS: Record<string, typeof Zap> = {
  trigger: Zap, action: Play, ai: Brain, 'human-gate': UserCheck, router: GitBranch, end: CircleStop,
};

const workingLabel = ref('');
const workingConfig = ref<Record<string, unknown>>({});
const workingRetry = ref<RetryPolicy | null>(null);
const workingTimeout = ref<TimeoutPolicy | null>(null);
const workingCompensate = ref<CompensationBlock | null>(null);
/** Rows being edited for a map-shaped `inputs` (keeps half-typed rows with no field name yet). */
const mapInputRows = ref<Mapping[] | null>(null);

watch(
  () => props.node,
  (node) => {
    workingLabel.value = node?.label ?? '';
    workingConfig.value = node ? JSON.parse(JSON.stringify(node.config ?? {})) : {};
    workingRetry.value = node?.retry ? { ...node.retry } : legacyConfigRetry(workingConfig.value);
    // Older canvas versions kept retry inside config (`config.retry: {count, delaySeconds, backoff}`),
    // which the engine never read. It moves to the node-level block when the node is saved.
    if (workingRetry.value && !node?.retry) delete workingConfig.value.retry;
    // Likewise `config.timeoutSeconds`: the engine reads the node-level `timeout.seconds`.
    const legacyTimeout = Number(workingConfig.value.timeoutSeconds);
    workingTimeout.value = node?.timeout
      ? { ...node.timeout }
      : Number.isFinite(legacyTimeout) && legacyTimeout > 0 ? { seconds: legacyTimeout } : null;
    if (!node?.timeout && 'timeoutSeconds' in workingConfig.value) delete workingConfig.value.timeoutSeconds;
    workingCompensate.value = node?.compensate ? { ...node.compensate } : null;
    mapInputRows.value = null;
    activeTab.value = 'Basic';
  },
  { immediate: true },
);

const nodeType = computed(() => props.node?.type ?? '');
const icon = computed(() => ICONS[nodeType.value] ?? Settings);
const hasOutputs = computed(() => nodeType.value === 'action' || nodeType.value === 'ai');
const visibleTabs = computed(() => TABS.filter((t) => t !== 'Outputs' || hasOutputs.value));

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

// Action inputs are a map in the flowamaz/v1 spec (`inputs: { url: …, body: … }`, edited by
// ActionConfig); other node types keep the canvas row list. Never turn one into the other.
const inputsAsMap = computed(
  () => isPlainObject(workingConfig.value.inputs) || (nodeType.value === 'action' && !Array.isArray(workingConfig.value.inputs)),
);

const inputMappings = computed<Mapping[]>(() => {
  const raw = workingConfig.value.inputs;
  if (!inputsAsMap.value) return Array.isArray(raw) ? (raw as Mapping[]) : [];
  if (mapInputRows.value) return mapInputRows.value;
  return Object.entries(isPlainObject(raw) ? raw : {}).map(([field, value]) => ({
    field,
    value: typeof value === 'string' ? value : JSON.stringify(value),
  }));
});
const outputMappings = computed<Mapping[]>(() => {
  const raw = workingConfig.value.outputs;
  return Array.isArray(raw) ? (raw as Mapping[]) : [];
});

function updateConfig(next: Record<string, unknown>): void {
  workingConfig.value = next;
  // ActionConfig may rewrite the inputs map; re-derive the rows from it.
  mapInputRows.value = null;
}

function setInputs(rows: Mapping[]): void {
  if (!inputsAsMap.value) {
    workingConfig.value = { ...workingConfig.value, inputs: rows };
    return;
  }
  mapInputRows.value = rows;
  const previous = isPlainObject(workingConfig.value.inputs) ? workingConfig.value.inputs : {};
  const next: Record<string, unknown> = {};
  for (const row of rows) {
    const field = row.field.trim();
    if (!field) continue;
    const prev = previous[field];
    // Keep non-string YAML values (numbers, objects) as they were unless the text was edited.
    const unchanged = prev !== undefined && (typeof prev === 'string' ? prev : JSON.stringify(prev)) === row.value;
    next[field] = unchanged ? prev : row.value;
  }
  workingConfig.value = { ...workingConfig.value, inputs: next };
}

function setOutputs(rows: Mapping[]): void {
  workingConfig.value = { ...workingConfig.value, outputs: rows };
}

// ── Advanced tab helpers ─────────────────────────────────────────────────────
// The form shows retries after the first try, the wait before the first retry, and whether waits double
// (exponential, multiplier 2) or stay the same (fixed, multiplier 1); it saves the documented
// `retry: { max_attempts, backoff_seconds, backoff_multiplier }` block. 0 retries removes the block.
type RetryForm = { count: number; delaySeconds: number; backoff: 'exponential' | 'fixed' };

function legacyConfigRetry(config: Record<string, unknown>): RetryPolicy | null {
  const old = config.retry as { count?: unknown; delaySeconds?: unknown; backoff?: unknown } | undefined;
  if (!old || typeof old !== 'object') return null;
  const count = Number(old.count ?? 0);
  if (!Number.isFinite(count) || count <= 0) return null;
  return {
    maxAttempts: count + 1,
    backoffSeconds: Math.max(0, Number(old.delaySeconds ?? 5) || 0),
    backoffMultiplier: old.backoff === 'exponential' ? 2 : 1,
  };
}

const retry = computed<RetryForm>(() => workingRetry.value
  ? {
      count: Math.max(0, workingRetry.value.maxAttempts - 1),
      delaySeconds: workingRetry.value.backoffSeconds,
      backoff: workingRetry.value.backoffMultiplier > 1 ? 'exponential' : 'fixed',
    }
  : { count: 0, delaySeconds: 5, backoff: 'exponential' });
const otherNodes = computed(() => props.nodeOptions.filter((n) => n.id !== props.node?.id));

function setTimeout_(updates: Partial<TimeoutPolicy>): void {
  const next: TimeoutPolicy = { seconds: 0, ...(workingTimeout.value ?? {}), ...updates };
  const seconds = Number.isFinite(next.seconds) ? Math.max(0, Math.round(next.seconds)) : 0;
  const onTimeout = next.onTimeout?.trim() || undefined;
  workingTimeout.value = seconds > 0 || onTimeout ? { seconds, ...(onTimeout ? { onTimeout } : {}) } : null;
}

function setCompensate(strategy: string, nodeId?: string): void {
  if (strategy !== 'backward' && strategy !== 'forward' && strategy !== 'pivot') {
    workingCompensate.value = null;
    return;
  }
  const undo = (nodeId ?? workingCompensate.value?.nodeId)?.trim() || undefined;
  workingCompensate.value = { strategy, ...(undo ? { nodeId: undo } : {}) };
}
const tags = computed<string>(() => ((workingConfig.value.tags as string[]) ?? []).join(', '));

function setRetry(updates: Partial<RetryForm>): void {
  const next = { ...retry.value, ...updates };
  if (!Number.isFinite(next.count) || next.count <= 0) {
    workingRetry.value = null;
    return;
  }
  const previous = workingRetry.value;
  workingRetry.value = {
    maxAttempts: Math.round(next.count) + 1,
    backoffSeconds: Math.max(0, Math.round(next.delaySeconds) || 0),
    // Keep a hand-written multiplier (e.g. 1.5) unless the backoff kind was changed here.
    backoffMultiplier: next.backoff === 'fixed'
      ? 1
      : previous && previous.backoffMultiplier > 1 ? previous.backoffMultiplier : 2,
  };
}

function setTags(value: string): void {
  const list = value.split(',').map((t) => t.trim()).filter(Boolean);
  workingConfig.value = { ...workingConfig.value, tags: list };
}

function save(): void {
  if (!props.node) return;
  emit('save', {
    id: props.node.id,
    label: workingLabel.value,
    config: workingConfig.value,
    retry: workingRetry.value,
    timeout: workingTimeout.value,
    compensate: workingCompensate.value,
  });
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') emit('close');
}
</script>

<template>
  <Transition
    enter-active-class="transition-transform duration-[250ms] ease-out"
    enter-from-class="translate-x-full"
    leave-active-class="transition-transform duration-[250ms] ease-in"
    leave-to-class="translate-x-full"
  >
    <aside
      v-if="open && node"
      class="fixed inset-y-0 right-0 z-50 flex w-[480px] max-w-full flex-col border-l border-gray-700 bg-gray-900 shadow-2xl"
      role="dialog"
      aria-label="Node configuration"
      tabindex="-1"
      @keydown="onKeydown"
    >
      <!-- Header -->
      <div class="flex items-center gap-2 border-b border-gray-700 px-4 py-3">
        <component
          :is="icon"
          class="h-5 w-5 text-indigo-400"
        />
        <input
          v-model="workingLabel"
          class="flex-1 rounded-md border border-transparent bg-transparent px-1 py-0.5 text-sm font-semibold text-gray-100 hover:border-gray-700 focus:border-indigo-400 focus:outline-none"
          aria-label="Node label"
        >
        <button
          type="button"
          class="rounded p-1 text-gray-400 hover:bg-gray-800 hover:text-gray-100"
          aria-label="Close"
          @click="emit('close')"
        >
          <X class="h-4 w-4" />
        </button>
      </div>

      <!-- Tabs -->
      <div class="flex border-b border-gray-700 text-xs">
        <button
          v-for="tab in visibleTabs"
          :key="tab"
          type="button"
          :class="[
            'flex-1 py-2 font-medium transition-colors',
            activeTab === tab ? 'border-b-2 border-indigo-400 text-indigo-400' : 'text-gray-500 hover:text-gray-300',
          ]"
          @click="activeTab = tab"
        >
          {{ tab }}
        </button>
      </div>

      <!-- Content -->
      <div class="flex-1 overflow-y-auto p-4">
        <!-- Keyed by node id so per-node UI state (e.g. the gate's timeout unit) never leaks between nodes. -->
        <template v-if="activeTab === 'Basic'">
          <TriggerConfig
            v-if="nodeType === 'trigger'"
            :key="`trigger-${node.id}`"
            :config="workingConfig"
            :webhook-url="webhookUrl"
            :workflow-trigger-type="workflowTriggerType"
            @update="updateConfig"
          />
          <ActionConfig
            v-else-if="nodeType === 'action'"
            :key="`action-${node.id}`"
            :config="workingConfig"
            :variables="variables"
            @update="updateConfig"
          />
          <HumanGateConfig
            v-else-if="nodeType === 'human-gate'"
            :key="`gate-${node.id}`"
            :config="workingConfig"
            @update="updateConfig"
          />
          <RouterConditionBuilder
            v-else-if="nodeType === 'router'"
            :config="workingConfig"
            :variables="variables"
            @update="updateConfig"
          />
          <AiNodeConfig
            v-else-if="nodeType === 'ai'"
            :config="workingConfig"
            :variables="variables"
            @update="updateConfig"
          />
          <EndConfig
            v-else-if="nodeType === 'end'"
            :config="workingConfig"
            @update="updateConfig"
          />
          <p
            v-else
            class="text-sm text-gray-500"
          >
            No basic configuration for this node type.
          </p>
        </template>

        <template v-else-if="activeTab === 'Inputs'">
          <InputMappingEditor
            :model-value="inputMappings"
            :variables="variables"
            field-label="Node input"
            value-label="Workflow variable / literal"
            field-placeholder="input field"
            @update:model-value="setInputs"
          />
        </template>

        <template v-else-if="activeTab === 'Outputs'">
          <InputMappingEditor
            :model-value="outputMappings"
            :variables="variables"
            field-label="Output key"
            value-label="Store in variable"
            field-placeholder="response.body"
            @update:model-value="setOutputs"
          />
        </template>

        <template v-else-if="activeTab === 'Advanced'">
          <div class="space-y-4">
            <div>
              <label class="mb-1 block text-xs font-medium text-gray-300">Retry policy</label>
              <div class="grid grid-cols-3 gap-2">
                <div>
                  <span class="text-[11px] text-gray-500">Retries</span>
                  <input
                    :value="retry.count"
                    type="number"
                    min="0"
                    class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                    @input="setRetry({ count: Number(($event.target as HTMLInputElement).value) })"
                  >
                </div>
                <div>
                  <span class="text-[11px] text-gray-500">Delay (s)</span>
                  <input
                    :value="retry.delaySeconds"
                    type="number"
                    min="0"
                    class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                    @input="setRetry({ delaySeconds: Number(($event.target as HTMLInputElement).value) })"
                  >
                </div>
                <div>
                  <span class="text-[11px] text-gray-500">Backoff</span>
                  <select
                    :value="retry.backoff"
                    class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                    @change="setRetry({ backoff: ($event.target as HTMLSelectElement).value === 'fixed' ? 'fixed' : 'exponential' })"
                  >
                    <option value="exponential">
                      exponential
                    </option>
                    <option value="fixed">
                      fixed
                    </option>
                  </select>
                </div>
              </div>
            </div>

            <div>
              <label class="mb-1 block text-xs font-medium text-gray-300">Timeout</label>
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <span class="text-[11px] text-gray-500">Seconds (0 = none)</span>
                  <input
                    :value="workingTimeout?.seconds ?? 0"
                    type="number"
                    min="0"
                    data-test="timeout-seconds"
                    class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                    @input="setTimeout_({ seconds: Number(($event.target as HTMLInputElement).value) })"
                  >
                </div>
                <div>
                  <span class="text-[11px] text-gray-500">On timeout, go to</span>
                  <select
                    :value="workingTimeout?.onTimeout ?? ''"
                    data-test="timeout-goto"
                    class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                    @change="setTimeout_({ onTimeout: ($event.target as HTMLSelectElement).value })"
                  >
                    <option value="">
                      fail the step
                    </option>
                    <option
                      v-for="n in otherNodes"
                      :key="n.id"
                      :value="n.id"
                    >
                      {{ n.label === n.id ? n.id : `${n.label} (${n.id})` }}
                    </option>
                  </select>
                </div>
              </div>
            </div>

            <div>
              <label class="mb-1 block text-xs font-medium text-gray-300">Compensation (undo if the run fails later)</label>
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <span class="text-[11px] text-gray-500">Strategy</span>
                  <select
                    :value="workingCompensate?.strategy ?? ''"
                    data-test="compensate-strategy"
                    class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                    @change="setCompensate(($event.target as HTMLSelectElement).value)"
                  >
                    <option value="">
                      none
                    </option>
                    <option value="backward">
                      backward (undo in reverse)
                    </option>
                    <option value="forward">
                      forward (carry on)
                    </option>
                    <option value="pivot">
                      pivot
                    </option>
                  </select>
                </div>
                <div>
                  <span class="text-[11px] text-gray-500">Undo step</span>
                  <select
                    :value="workingCompensate?.nodeId ?? ''"
                    :disabled="!workingCompensate"
                    data-test="compensate-node"
                    class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none disabled:opacity-50"
                    @change="setCompensate(workingCompensate?.strategy ?? '', ($event.target as HTMLSelectElement).value)"
                  >
                    <option value="">
                      none
                    </option>
                    <option
                      v-for="n in otherNodes"
                      :key="n.id"
                      :value="n.id"
                    >
                      {{ n.label === n.id ? n.id : `${n.label} (${n.id})` }}
                    </option>
                  </select>
                </div>
              </div>
            </div>

            <div>
              <label class="mb-1 block text-xs font-medium text-gray-300">Tags (comma separated)</label>
              <input
                :value="tags"
                type="text"
                placeholder="finance, critical"
                class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                @input="setTags(($event.target as HTMLInputElement).value)"
              >
            </div>
          </div>
        </template>
      </div>

      <!-- Footer -->
      <div class="flex justify-end gap-2 border-t border-gray-700 px-4 py-3">
        <button
          type="button"
          class="rounded-md px-3 py-1.5 text-sm font-medium text-gray-300 hover:bg-gray-800"
          @click="emit('close')"
        >
          Cancel
        </button>
        <button
          type="button"
          data-test="save-node"
          class="rounded-md bg-indigo-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-400"
          @click="save"
        >
          Save node
        </button>
      </div>
    </aside>
  </Transition>
</template>
