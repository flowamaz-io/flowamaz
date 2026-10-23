<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { connectorService, type ConnectorDefinition } from '@/services/connector.service';
import {
  credentialService,
  isOAuthConnector,
  manualCredentialTypes,
  parseManifest,
  type CredentialAlias,
} from '@/services/credential.service';
import VariableAutocomplete, { type WorkflowVariable } from '../shared/VariableAutocomplete.vue';

type Config = Record<string, unknown>;

const props = defineProps<{ config: Config; workspaceId: string; variables: WorkflowVariable[] }>();
const emit = defineEmits<{ update: [config: Config] }>();

const connectors = ref<ConnectorDefinition[]>([]);
const credentials = ref<CredentialAlias[]>([]);
const loading = ref(false);
const loadError = ref<string | null>(null);

// Canonical keys are the flowamaz/v1 spec (connector_id / operation / inputs / credential); older
// canvas drafts used connector / action / fields, so read both and always write the spec keys.
const connectorId = computed<string>(() => (props.config.connector_id as string) ?? (props.config.connector as string) ?? '');
const operationId = computed<string>(() => (props.config.operation as string) ?? (props.config.action as string) ?? '');
const credentialRef = computed<string>(() => (props.config.credential as string) ?? '');
const inputValues = computed<Record<string, string>>(
  () => (props.config.inputs as Record<string, string>) ?? (props.config.fields as Record<string, string>) ?? {},
);

function patch(updates: Config): void {
  const next: Config = { ...props.config, ...updates };
  delete next.connector;
  delete next.action;
  delete next.fields;
  emit('update', next);
}

const selectedConnector = computed(() => connectors.value.find((c) => c.connectorId === connectorId.value));
/** A connector named in the YAML that this workspace can't load — shown as-is, never cleared. */
const unknownConnector = computed(() => !loading.value && !!connectorId.value && !selectedConnector.value);
const manifest = computed(() => parseManifest(selectedConnector.value));
const operations = computed(() => (manifest.value.operations ?? []).filter((o) => !o.internal));
const selectedOperation = computed(() => operations.value.find((o) => o.id === operationId.value));
const needsCredential = computed(() => selectedOperation.value?.requires_credential === true);
const acceptsCredential = computed(
  () => isOAuthConnector(manifest.value) || manualCredentialTypes(manifest.value).length > 0,
);

const inputFields = computed(() => {
  const schema = selectedOperation.value?.input_schema;
  const required = new Set(schema?.required ?? []);
  return Object.entries(schema?.properties ?? {}).map(([key, s]) => ({
    key,
    type: s.type ?? 'string',
    description: s.description ?? '',
    required: required.has(key),
  }));
});

const missingRequired = computed(() =>
  inputFields.value.filter((f) => f.required && !(inputValues.value[f.key] ?? '').toString().trim()).map((f) => f.key),
);

function setInput(key: string, value: string): void {
  patch({ inputs: { ...inputValues.value, [key]: value } });
}

async function loadCredentials(): Promise<void> {
  if (!connectorId.value || !acceptsCredential.value) {
    credentials.value = [];
    return;
  }
  try {
    credentials.value = (await credentialService.list(props.workspaceId, connectorId.value)).filter((c) => c.isActive);
  } catch {
    credentials.value = [];
  }
}

watch(connectorId, loadCredentials);

onMounted(async () => {
  loading.value = true;
  try {
    // Official connectors are available on every plan; others appear once installed from the Library.
    const all = await connectorService.getAllConnectors(props.workspaceId);
    connectors.value = all.filter((c) => c.isOfficial || c.isInstalled);
    await loadCredentials();
  } catch {
    connectors.value = [];
    loadError.value = 'Could not load connectors. Check your connection and reopen this panel.';
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
        @change="patch({ connector_id: ($event.target as HTMLSelectElement).value, operation: '', inputs: {}, credential: undefined })"
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
        <option
          v-if="unknownConnector"
          :value="connectorId"
        >
          {{ connectorId }} (not available in this workspace)
        </option>
      </select>
      <p
        v-if="unknownConnector && !loadError"
        data-test="action-unknown-connector"
        class="mt-1 text-xs text-amber-400"
      >
        The connector "{{ connectorId }}" isn't installed in this workspace, so its fields can't be edited here.
        Its settings are kept exactly as they are in the YAML — install it from the Library to edit them in this panel.
      </p>
      <p
        v-if="loadError"
        class="mt-1 text-xs text-red-400"
      >
        {{ loadError }}
      </p>
    </div>

    <div v-if="selectedConnector">
      <label class="mb-1 block text-xs font-medium text-gray-300">Action</label>
      <select
        :value="operationId"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ operation: ($event.target as HTMLSelectElement).value, inputs: {} })"
      >
        <option value="">
          Select an action
        </option>
        <option
          v-for="op in operations"
          :key="op.id"
          :value="op.id"
        >
          {{ op.name ?? op.id }}
        </option>
        <option
          v-if="operationId && !selectedOperation"
          :value="operationId"
        >
          {{ operationId }} (unknown action)
        </option>
      </select>
      <p
        v-if="selectedOperation?.description"
        class="mt-1 text-xs text-gray-500"
      >
        {{ selectedOperation.description }}
      </p>
    </div>

    <div v-if="selectedConnector && acceptsCredential">
      <label class="mb-1 block text-xs font-medium text-gray-300">
        Credential
        <span
          v-if="!needsCredential"
          class="ml-1 font-normal text-gray-500"
        >optional</span>
      </label>
      <select
        :value="credentialRef"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ credential: ($event.target as HTMLSelectElement).value || undefined })"
      >
        <option value="">
          {{ credentials.length === 1 ? `Workspace default (${credentials[0]!.name})` : 'Select a credential' }}
        </option>
        <option
          v-for="c in credentials"
          :key="c.id"
          :value="c.name"
        >
          {{ c.name }}
        </option>
      </select>
      <p
        v-if="credentials.length === 0"
        class="mt-1 text-xs text-amber-400"
      >
        No {{ selectedConnector.displayName }} credential yet.
        <RouterLink
          to="/settings/credentials"
          class="underline hover:text-amber-300"
        >
          Add one in Settings → Credentials
        </RouterLink>
        <template v-if="needsCredential">
          — this step can't run without it.
        </template>
      </p>
      <p
        v-else-if="!credentialRef && credentials.length > 1 && needsCredential"
        class="mt-1 text-xs text-amber-400"
      >
        This workspace has several {{ selectedConnector.displayName }} credentials — pick the one this step should use.
      </p>
    </div>

    <div
      v-for="field in inputFields"
      :key="field.key"
    >
      <label class="mb-1 block text-xs font-medium text-gray-300">
        {{ field.key }}
        <span
          v-if="field.required"
          class="text-red-400"
          aria-label="required"
        >*</span>
        <span class="ml-1 font-normal text-gray-500">{{ field.type }}</span>
      </label>
      <VariableAutocomplete
        :model-value="inputValues[field.key] ?? ''"
        :variables="variables"
        :placeholder="field.description || 'value or {{ variables.name }}'"
        @update:model-value="setInput(field.key, $event)"
      />
    </div>

    <p
      v-if="selectedOperation && missingRequired.length > 0"
      class="text-xs text-amber-400"
    >
      Required: {{ missingRequired.join(', ') }}
    </p>
  </div>
</template>
