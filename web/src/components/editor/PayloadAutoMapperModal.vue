<script setup lang="ts">
import { ref, computed } from 'vue';
import { connectorService } from '@/services/connector.service';
import type { FieldMapping } from '@/services/connector.service';

interface Props {
  modelValue: boolean;
  workspaceId: string;
  connectorId: string;
  operationId: string;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  mapped: [mappings: FieldMapping[]];
}>();

const sampleJson = ref('');
const loading = ref(false);
const error = ref<string | null>(null);
const mappings = ref<FieldMapping[]>([]);
const unmappedFields = ref<string[]>([]);
const editedExpressions = ref<Record<string, string>>({});
const hasMappings = computed(() => mappings.value.length > 0 || unmappedFields.value.length > 0);

function close(): void {
  emit('update:modelValue', false);
}

function confidenceColor(confidence: number): string {
  if (confidence >= 0.8) return 'text-emerald-600';
  if (confidence >= 0.5) return 'text-amber-600';
  return 'text-rose-600';
}

function confidenceBadge(confidence: number): string {
  return `${Math.round(confidence * 100)}%`;
}

function expressionFor(mapping: FieldMapping): string {
  return editedExpressions.value[mapping.fieldName] ?? mapping.suggestedExpression;
}

function onExpressionChange(fieldName: string, value: string): void {
  editedExpressions.value = { ...editedExpressions.value, [fieldName]: value };
}

async function runAutoMap(): Promise<void> {
  error.value = null;
  mappings.value = [];
  unmappedFields.value = [];
  editedExpressions.value = {};

  let parsed: object;
  try {
    parsed = JSON.parse(sampleJson.value) as object;
  } catch {
    error.value = 'Invalid JSON. Please paste valid JSON from the connector response.';
    return;
  }

  loading.value = true;
  try {
    const result = await connectorService.autoMap(
      props.workspaceId,
      props.connectorId,
      props.operationId,
      parsed,
    );
    mappings.value = result.mappings;
    unmappedFields.value = result.unmappedFields;
  } catch (err) {
    error.value = err instanceof Error
      ? err.message
      : 'Auto-map failed. Check your connection and try again.';
  } finally {
    loading.value = false;
  }
}

function applyMappings(): void {
  const confirmed: FieldMapping[] = mappings.value.map((m) => ({
    ...m,
    suggestedExpression: expressionFor(m),
  }));
  emit('mapped', confirmed);
  close();
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="modelValue"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
      @click.self="close"
    >
      <div class="w-full max-w-xl rounded-xl bg-white shadow-2xl flex flex-col max-h-[90vh]">
        <!-- Header -->
        <div class="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 class="text-base font-semibold text-slate-800">Paste a Sample Payload</h2>
          <button
            type="button"
            class="text-slate-400 hover:text-slate-600 text-xl leading-none"
            @click="close"
          >
            ×
          </button>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto p-5 space-y-4">
          <!-- Textarea -->
          <div>
            <label class="block text-xs font-medium text-slate-600 mb-1">
              Paste JSON from <span class="font-semibold text-indigo-600">{{ connectorId }}</span> here…
            </label>
            <textarea
              v-model="sampleJson"
              rows="6"
              placeholder='{"contact_id": "C001", "amount": 150}'
              class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm font-mono text-slate-800 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200 resize-y"
            />
          </div>

          <!-- Error -->
          <p
            v-if="error"
            class="text-sm text-rose-600 bg-rose-50 rounded-lg px-3 py-2"
          >
            {{ error }}
          </p>

          <!-- Results table -->
          <div v-if="hasMappings">
            <h3 class="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">
              Mapped Fields
            </h3>
            <div
              v-if="mappings.length > 0"
              class="overflow-x-auto rounded-lg border border-slate-100"
            >
              <table class="w-full text-sm">
                <thead class="bg-slate-50 text-xs text-slate-500 uppercase">
                  <tr>
                    <th class="px-3 py-2 text-left font-medium">Field</th>
                    <th class="px-3 py-2 text-left font-medium">Expression</th>
                    <th class="px-3 py-2 text-right font-medium">Confidence</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                  <tr
                    v-for="mapping in mappings"
                    :key="mapping.fieldName"
                    class="hover:bg-slate-50"
                  >
                    <td class="px-3 py-2 font-mono text-slate-700">{{ mapping.fieldName }}</td>
                    <td class="px-3 py-2">
                      <input
                        :value="expressionFor(mapping)"
                        type="text"
                        class="w-full rounded border border-slate-200 px-2 py-1 text-xs font-mono text-slate-800 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
                        @input="onExpressionChange(mapping.fieldName, ($event.target as HTMLInputElement).value)"
                      >
                    </td>
                    <td class="px-3 py-2 text-right">
                      <span
                        class="font-semibold text-xs"
                        :class="confidenceColor(mapping.confidence)"
                      >
                        {{ confidenceBadge(mapping.confidence) }}
                      </span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- Unmapped fields -->
            <div
              v-if="unmappedFields.length > 0"
              class="mt-3"
            >
              <h3 class="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-1">
                Unmapped Fields
              </h3>
              <div class="flex flex-wrap gap-2">
                <span
                  v-for="field in unmappedFields"
                  :key="field"
                  class="inline-flex items-center rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-mono text-slate-500"
                >
                  {{ field }}
                </span>
              </div>
              <p class="mt-1 text-xs text-slate-400">
                These fields had no matching key in your sample payload. Add them manually in the input fields.
              </p>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="flex items-center justify-between px-5 py-4 border-t border-slate-100 gap-3">
          <button
            type="button"
            class="text-sm text-slate-500 hover:text-slate-700"
            @click="close"
          >
            Cancel
          </button>
          <div class="flex items-center gap-2">
            <button
              type="button"
              :disabled="loading || !sampleJson.trim()"
              class="rounded-lg bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5"
              @click="runAutoMap"
            >
              <span
                v-if="loading"
                class="inline-block h-3.5 w-3.5 animate-spin rounded-full border-2 border-white border-t-transparent"
              />
              Auto-Map
            </button>
            <button
              v-if="mappings.length > 0"
              type="button"
              class="rounded-lg bg-emerald-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-emerald-700"
              @click="applyMappings"
            >
              Apply Mappings
            </button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>
