<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { connectorService, type ConnectorDefinition } from '@/services/connector.service';
import VariableAutocomplete, { type WorkflowVariable } from '../shared/VariableAutocomplete.vue';

type Config = Record<string, unknown>;

interface ManifestOperation {
  id?: string;
  name?: string;
  input_schema?: { properties?: Record<string, { description?: string; type?: string }> };
}

const props = defineProps<{ config: Config; workspaceId: string; variables: WorkflowVariable[] }>();
const emit = defineEmits<{ update: [config: Config] }>();

const connectors = ref<ConnectorDefinition[]>([]);
const loading = ref(false);

const connectorId = computed<string>(() => (props.config.connector as string) ?? '');
const operationId = computed<string>(() => (props.config.action as string) ?? '');
const fieldValues = computed<Record<string, string>>(
  () => (props.config.fields as Record<string, string>) ?? {},
);

function patch(updates: Config): void {
  emit('update', { ...props.config, ...updates });
}

const selectedConnector = computed(() =>
  connectors.value.find((c) => c.connectorId === connectorId.value),
);

const operations = computed<ManifestOperation[]>(() => {
  if (!selectedConnector.value) return [];
  try {
    const manifest = JSON.parse(selectedConnector.value.manifestJson) as { operations?: ManifestOperation[] };
    return manifest.operations ?? [];
  } catch {
    return [];
  }
});

const inputFields = computed(() => {
  const op = operations.value.find((o) => (o.id ?? o.name) === operationId.value);
  return Object.entries(op?.input_schema?.properties ?? {}).map(([key, schema]) => ({
    key,
    type: schema.type ?? 'string',
    description: schema.description ?? '',
  }));
});

function setField(key: string, value: string): void {
  patch({ fields: { ...fieldValues.value, [key]: value } });
}

onMounted(async () => {
  loading.value = true;
  try {
    connectors.value = await connectorService.getInstalledConnectors(props.workspaceId);
  } catch {
    connectors.value = [];
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="space-y-4">
    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Connector</label>
      <select
        :value="connectorId"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ connector: ($event.target as HTMLSelectElement).value, action: '', fields: {} })"
      >
        <option value="">
          {{ loading ? 'Loading connectors…' : 'Select a connector' }}
        </option>
        <option
          v-for="c in connectors"
          :key="c.connectorId"
          :value="c.connectorId"
        >
          {{ c.displayName }}
        </option>
      </select>
      <p
        v-if="!loading && connectors.length === 0"
        class="mt-1 text-xs text-gray-500"
      >
        No connectors installed. Install one from the Library to configure this action.
      </p>
    </div>

    <div v-if="selectedConnector">
      <label class="mb-1 block text-xs font-medium text-gray-300">Action</label>
      <select
        :value="operationId"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ action: ($event.target as HTMLSelectElement).value, fields: {} })"
      >
        <option value="">
          Select an action
        </option>
        <option
          v-for="op in operations"
          :key="op.id ?? op.name"
          :value="op.id ?? op.name"
        >
          {{ op.name ?? op.id }}
        </option>
      </select>
    </div>

    <div
      v-for="field in inputFields"
      :key="field.key"
    >
      <label class="mb-1 block text-xs font-medium text-gray-300">
        {{ field.key }}
        <span class="ml-1 font-normal text-gray-500">{{ field.type }}</span>
      </label>
      <VariableAutocomplete
        :model-value="fieldValues[field.key] ?? ''"
        :variables="variables"
        :placeholder="field.description || `value or \${variable}`"
        @update:model-value="setField(field.key, $event)"
      />
    </div>
  </div>
</template>
