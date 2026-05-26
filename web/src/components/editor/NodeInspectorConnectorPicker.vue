<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import PayloadAutoMapperModal from './PayloadAutoMapperModal.vue';
import { connectorService } from '@/services/connector.service';
import type { ConnectorDefinition, FieldMapping } from '@/services/connector.service';

interface ConnectorManifest {
  operations?: Array<{
    id?: string;
    name?: string;
    input_schema?: {
      properties?: Record<string, { description?: string; type?: string }>;
    };
  }>;
}

interface ConnectorValue {
  connectorId?: string;
  operationId?: string;
  inputFields?: Record<string, string>;
}

interface Props {
  workspaceId: string;
  modelValue: ConnectorValue;
}

const props = defineProps<Props>();
const emit = defineEmits<{ 'update:modelValue': [value: ConnectorValue] }>();

const installedConnectors = ref<ConnectorDefinition[]>([]);
const loading = ref(false);
const mapperOpen = ref(false);

const selectedConnector = computed<ConnectorDefinition | undefined>(() =>
  installedConnectors.value.find((c) => c.connectorId === props.modelValue.connectorId),
);

const manifest = computed<ConnectorManifest>(() => {
  if (!selectedConnector.value) return {};
  try {
    return JSON.parse(selectedConnector.value.manifestJson) as ConnectorManifest;
  } catch {
    return {};
  }
});

const operations = computed(() => manifest.value.operations ?? []);

const selectedOperation = computed(() =>
  operations.value.find(
    (op) => (op.id ?? op.name) === props.modelValue.operationId,
  ),
);

const inputFields = computed<Record<string, { description?: string; type?: string }>>(
  () => selectedOperation.value?.input_schema?.properties ?? {},
);

async function loadInstalled(): Promise<void> {
  if (!props.workspaceId) return;
  loading.value = true;
  try {
    installedConnectors.value = await connectorService.getInstalledConnectors(props.workspaceId);
  } catch {
    installedConnectors.value = [];
  } finally {
    loading.value = false;
  }
}

function onConnectorChange(event: Event): void {
  const val = (event.target as HTMLSelectElement).value;
  emit('update:modelValue', { connectorId: val || undefined, operationId: undefined, inputFields: {} });
}

function onOperationChange(event: Event): void {
  const val = (event.target as HTMLSelectElement).value;
  emit('update:modelValue', { ...props.modelValue, operationId: val || undefined, inputFields: {} });
}

function onFieldChange(fieldName: string, value: string): void {
  emit('update:modelValue', {
    ...props.modelValue,
    inputFields: { ...(props.modelValue.inputFields ?? {}), [fieldName]: value },
  });
}

function onMapped(mappings: FieldMapping[]): void {
  const patched: Record<string, string> = { ...(props.modelValue.inputFields ?? {}) };
  for (const m of mappings) {
    patched[m.fieldName] = m.suggestedExpression;
  }
  emit('update:modelValue', { ...props.modelValue, inputFields: patched });
}

watch(() => props.workspaceId, loadInstalled);
onMounted(loadInstalled);
</script>

<template>
  <div class="space-y-3">
    <!-- Loading -->
    <div
      v-if="loading"
      class="flex items-center gap-2 text-sm text-slate-500"
    >
      <FmSpinner size="sm" />
      <span>Loading connectors…</span>
    </div>

    <template v-else>
      <!-- Connector select -->
      <label class="block">
        <span class="text-xs font-medium text-slate-600">Connector</span>
        <select
          :value="modelValue.connectorId ?? ''"
          class="mt-1 w-full rounded-lg border border-slate-200 px-2 py-1.5 text-sm text-slate-800 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
          @change="onConnectorChange"
        >
          <option value="">— Select connector —</option>
          <option
            v-for="c in installedConnectors"
            :key="c.connectorId"
            :value="c.connectorId"
          >
            {{ c.displayName }}
          </option>
        </select>
      </label>

      <!-- Operation select (only when connector selected) -->
      <label
        v-if="modelValue.connectorId && operations.length > 0"
        class="block"
      >
        <span class="text-xs font-medium text-slate-600">Operation</span>
        <select
          :value="modelValue.operationId ?? ''"
          class="mt-1 w-full rounded-lg border border-slate-200 px-2 py-1.5 text-sm text-slate-800 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
          @change="onOperationChange"
        >
          <option value="">— Select operation —</option>
          <option
            v-for="op in operations"
            :key="op.id ?? op.name"
            :value="op.id ?? op.name"
          >
            {{ op.name ?? op.id }}
          </option>
        </select>
      </label>

      <!-- Dynamic input fields -->
      <template v-if="modelValue.operationId && Object.keys(inputFields).length > 0">
        <label
          v-for="(field, fieldName) in inputFields"
          :key="fieldName"
          class="block"
        >
          <span class="text-xs font-medium text-slate-600 capitalize">{{ fieldName }}</span>
          <span
            v-if="field.description"
            class="ml-1 text-xs text-slate-400"
          >— {{ field.description }}</span>
          <input
            :value="modelValue.inputFields?.[fieldName] ?? ''"
            type="text"
            :placeholder="field.type ?? 'string'"
            class="mt-1 w-full rounded-lg border border-slate-200 px-2 py-1.5 text-sm text-slate-800 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
            @input="onFieldChange(fieldName, ($event.target as HTMLInputElement).value)"
          >
        </label>
      </template>

      <!-- Payload auto-mapper trigger -->
      <button
        v-if="modelValue.operationId"
        type="button"
        class="text-xs text-indigo-500 hover:text-indigo-700 underline underline-offset-2 text-left"
        @click="mapperOpen = true"
      >
        Paste a sample payload to auto-map fields ↗
      </button>

      <!-- Payload auto-mapper modal -->
      <PayloadAutoMapperModal
        v-if="mapperOpen"
        v-model="mapperOpen"
        :workspace-id="workspaceId"
        :connector-id="modelValue.connectorId ?? ''"
        :operation-id="modelValue.operationId ?? ''"
        @mapped="onMapped"
      />
    </template>
  </div>
</template>
