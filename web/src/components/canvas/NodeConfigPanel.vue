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

export interface ConfigNode {
  id: string;
  type: string;
  label: string;
  config: Record<string, unknown>;
}

const props = withDefaults(
  defineProps<{
    open: boolean;
    node: ConfigNode | null;
    workspaceId: string;
    variables?: WorkflowVariable[];
    webhookUrl?: string;
  }>(),
  { variables: () => [], webhookUrl: '' },
);

const emit = defineEmits<{
  close: [];
  save: [payload: { id: string; label: string; config: Record<string, unknown> }];
}>();

type Tab = 'Basic' | 'Inputs' | 'Outputs' | 'Advanced';
const TABS: Tab[] = ['Basic', 'Inputs', 'Outputs', 'Advanced'];
const activeTab = ref<Tab>('Basic');

const ICONS: Record<string, typeof Zap> = {
  trigger: Zap, action: Play, ai: Brain, 'human-gate': UserCheck, router: GitBranch, end: CircleStop,
};

const workingLabel = ref('');
const workingConfig = ref<Record<string, unknown>>({});

watch(
  () => props.node,
  (node) => {
    workingLabel.value = node?.label ?? '';
    workingConfig.value = node ? JSON.parse(JSON.stringify(node.config ?? {})) : {};
    activeTab.value = 'Basic';
  },
  { immediate: true },
);

const nodeType = computed(() => props.node?.type ?? '');
const icon = computed(() => ICONS[nodeType.value] ?? Settings);
const hasOutputs = computed(() => nodeType.value === 'action' || nodeType.value === 'ai');
const visibleTabs = computed(() => TABS.filter((t) => t !== 'Outputs' || hasOutputs.value));

const inputMappings = computed<Mapping[]>(() => (workingConfig.value.inputs as Mapping[]) ?? []);
const outputMappings = computed<Mapping[]>(() => (workingConfig.value.outputs as Mapping[]) ?? []);

function updateConfig(next: Record<string, unknown>): void {
  workingConfig.value = next;
}

function setInputs(rows: Mapping[]): void {
  workingConfig.value = { ...workingConfig.value, inputs: rows };
}

function setOutputs(rows: Mapping[]): void {
  workingConfig.value = { ...workingConfig.value, outputs: rows };
}

// ── Advanced tab helpers ─────────────────────────────────────────────────────
const retry = computed<{ count: number; delaySeconds: number; backoff: string }>(
  () => (workingConfig.value.retry as { count: number; delaySeconds: number; backoff: string })
    ?? { count: 0, delaySeconds: 5, backoff: 'exponential' },
);
const errorHandling = computed<string>(() => (workingConfig.value.errorHandling as string) ?? 'stop');
const timeoutSeconds = computed<number>(() => (workingConfig.value.timeoutSeconds as number) ?? 0);
const tags = computed<string>(() => ((workingConfig.value.tags as string[]) ?? []).join(', '));

function setRetry(updates: Partial<{ count: number; delaySeconds: number; backoff: string }>): void {
  workingConfig.value = { ...workingConfig.value, retry: { ...retry.value, ...updates } };
}

function setTags(value: string): void {
  const list = value.split(',').map((t) => t.trim()).filter(Boolean);
  workingConfig.value = { ...workingConfig.value, tags: list };
}

function save(): void {
  if (!props.node) return;
  emit('save', { id: props.node.id, label: workingLabel.value, config: workingConfig.value });
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
        <template v-if="activeTab === 'Basic'">
          <TriggerConfig
            v-if="nodeType === 'trigger'"
            :config="workingConfig"
            :webhook-url="webhookUrl"
            @update="updateConfig"
          />
          <ActionConfig
            v-else-if="nodeType === 'action'"
            :config="workingConfig"
            :workspace-id="workspaceId"
            :variables="variables"
            @update="updateConfig"
          />
          <HumanGateConfig
            v-else-if="nodeType === 'human-gate'"
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
                  <span class="text-[11px] text-gray-500">Count</span>
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
                    @change="setRetry({ backoff: ($event.target as HTMLSelectElement).value })"
                  >
                    <option value="exponential">
                      exponential
                    </option>
                    <option value="linear">
                      linear
                    </option>
                  </select>
                </div>
              </div>
            </div>

            <div>
              <label class="mb-1 block text-xs font-medium text-gray-300">Error handling</label>
              <select
                :value="errorHandling"
                class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                @change="updateConfig({ ...workingConfig, errorHandling: ($event.target as HTMLSelectElement).value })"
              >
                <option value="stop">
                  Stop the workflow
                </option>
                <option value="continue">
                  Continue
                </option>
                <option value="goto">
                  Go to node
                </option>
              </select>
            </div>

            <div>
              <label class="mb-1 block text-xs font-medium text-gray-300">Timeout (seconds, 0 = none)</label>
              <input
                :value="timeoutSeconds"
                type="number"
                min="0"
                class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
                @input="updateConfig({ ...workingConfig, timeoutSeconds: Number(($event.target as HTMLInputElement).value) })"
              >
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
          class="rounded-md bg-indigo-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-400"
          @click="save"
        >
          Save node
        </button>
      </div>
    </aside>
  </Transition>
</template>
