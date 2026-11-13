<script setup lang="ts">
import { toUserFacingError } from '@/utils/error.util';
import { computed, ref } from 'vue';
import { BookOpen, Plus, Save, Trash2 } from 'lucide-vue-next';
import { libraryService, type LibraryFormItem } from '@/services/library.service';
import { FIELD_TYPES, MAX_FIELDS, definitionProblems, keyFromLabel } from '@/utils/gateForm.util';
import type { GateFormField, GateFormFieldType } from '@/types';
import type { Config } from './configKeys';

// The fields an approver fills in at this gate (FUNCTIONAL.md §23.2, Library Forms). Stored on the gate as
// config.form.fields — a Library form is copied in, so changing the Library form later never changes this gate.
const props = defineProps<{ config: Config; workspaceId?: string }>();
const emit = defineEmits<{ update: [config: Config] }>();

function readFields(config: Config): GateFormField[] {
  const form = config.form as unknown;
  const raw = Array.isArray(form)
    ? form
    : form && typeof form === 'object' && Array.isArray((form as { fields?: unknown }).fields)
      ? (form as { fields: unknown[] }).fields
      : [];
  return raw
    .filter((f): f is Record<string, unknown> => f !== null && typeof f === 'object')
    .map((f) => ({
      key: typeof f.key === 'string' ? f.key : typeof f.id === 'string' ? f.id : '',
      label: typeof f.label === 'string' ? f.label : '',
      type: (typeof f.type === 'string' ? f.type.toLowerCase() : 'text') as GateFormFieldType,
      required: f.required === true,
      options: Array.isArray(f.options) ? f.options.map(String) : null,
      help: typeof f.help === 'string' ? f.help : null,
      min: typeof f.min === 'number' ? f.min : null,
      max: typeof f.max === 'number' ? f.max : null,
    }));
}

const fields = computed(() => readFields(props.config));
const problems = computed(() => definitionProblems(fields.value));

/** Writes the fields back in the YAML shape, leaving out empty optional keys. */
function write(next: GateFormField[]): void {
  const { form: _old, ...rest } = props.config;
  if (next.length === 0) {
    emit('update', rest);
    return;
  }
  const clean = next.map((f) => {
    const out: Record<string, unknown> = { key: f.key, label: f.label, type: f.type };
    if (f.required) out.required = true;
    if (f.type === 'select') out.options = f.options ?? [];
    if (f.help) out.help = f.help;
    if (f.type === 'number' && f.min != null) out.min = f.min;
    if (f.type === 'number' && f.max != null) out.max = f.max;
    return out;
  });
  emit('update', { ...rest, form: { fields: clean } });
}

function patchField(index: number, change: Partial<GateFormField>): void {
  const next = fields.value.map((f, i) => (i === index ? { ...f, ...change } : f));
  write(next);
}

function setLabel(index: number, label: string): void {
  const f = fields.value[index];
  // The key follows the label until someone edits the key itself.
  const follows = f.key === '' || f.key === keyFromLabel(f.label);
  patchField(index, follows ? { label, key: keyFromLabel(label) } : { label });
}

function setNumber(index: number, which: 'min' | 'max', raw: string): void {
  const n = raw.trim() === '' ? null : Number(raw);
  patchField(index, { [which]: n === null || Number.isFinite(n) ? n : null });
}

function addField(): void {
  const n = fields.value.length + 1;
  write([...fields.value, { key: `field_${n}`, label: `Field ${n}`, type: 'text', required: false }]);
}

function removeField(index: number): void {
  write(fields.value.filter((_, i) => i !== index));
}

// ── Library forms ───────────────────────────────────────────────────────────
const pickerOpen = ref(false);
const libraryForms = ref<LibraryFormItem[]>([]);
const libraryLoading = ref(false);
const libraryError = ref<string | null>(null);

async function openPicker(): Promise<void> {
  pickerOpen.value = !pickerOpen.value;
  if (!pickerOpen.value || !props.workspaceId) return;
  libraryLoading.value = true;
  libraryError.value = null;
  try {
    libraryForms.value = await libraryService.listForms(props.workspaceId);
  } catch (err: unknown) {
    libraryError.value = toUserFacingError(err).message || 'Library forms could not be loaded. Check your connection, then open the list again.';
  } finally {
    libraryLoading.value = false;
  }
}

function useForm(form: LibraryFormItem): void {
  write(form.fields.map((f) => ({ ...f })));
  pickerOpen.value = false;
}

const saving = ref(false);
const saveName = ref('');
const saveOpen = ref(false);
const saveMessage = ref<{ ok: boolean; text: string } | null>(null);

async function saveAsForm(): Promise<void> {
  if (!props.workspaceId || saveName.value.trim() === '') return;
  saving.value = true;
  saveMessage.value = null;
  try {
    const saved = await libraryService.saveForm(props.workspaceId, { name: saveName.value.trim(), fields: fields.value });
    saveMessage.value = { ok: true, text: `Saved as "${saved.name}". Other gates in this workspace can now use it from "Use a Library form".` };
    saveOpen.value = false;
    saveName.value = '';
  } catch (err: unknown) {
    saveMessage.value = {
      ok: false,
      text: toUserFacingError(err).message || 'The form was not saved. Check your connection and save again.',
    };
  } finally {
    saving.value = false;
  }
}

const outputExample = '{{ gate-id.output.form.key }}';

const inputClass = 'w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none';
</script>

<template>
  <div
    class="space-y-3 border-t border-gray-700 pt-4"
    data-test="gate-form-editor"
  >
    <div>
      <p class="text-xs font-medium text-gray-300">
        Approval form
      </p>
      <p class="mt-0.5 text-xs text-gray-500">
        Details the approver fills in before approving. Later steps use them as
        <code class="text-gray-400">{{ outputExample }}</code>.
      </p>
    </div>

    <p
      v-if="fields.length === 0"
      class="rounded-md border border-dashed border-gray-600 px-3 py-2 text-xs text-gray-400"
      data-test="gate-form-empty"
    >
      No form yet — the approver just approves or rejects. Add a field, or start from a Library form such as
      Purchase details.
    </p>

    <div
      v-for="(f, i) in fields"
      :key="i"
      class="space-y-2 rounded-md border border-gray-700 bg-gray-900/40 p-2"
      :data-test="`gate-form-field-${i}`"
    >
      <div class="flex items-center gap-2">
        <input
          :value="f.label"
          placeholder="Label, e.g. Cost centre"
          aria-label="Field label"
          :class="inputClass"
          @input="setLabel(i, ($event.target as HTMLInputElement).value)"
        >
        <button
          type="button"
          class="rounded p-1 text-gray-500 hover:bg-gray-700 hover:text-red-400"
          :aria-label="`Remove ${f.label || 'field'}`"
          @click="removeField(i)"
        >
          <Trash2 class="h-4 w-4" />
        </button>
      </div>
      <div class="grid grid-cols-2 gap-2">
        <select
          :value="f.type"
          aria-label="Field type"
          :class="inputClass"
          @change="patchField(i, { type: ($event.target as HTMLSelectElement).value as GateFormFieldType })"
        >
          <option
            v-for="t in FIELD_TYPES"
            :key="t.value"
            :value="t.value"
          >
            {{ t.label }}
          </option>
        </select>
        <input
          :value="f.key"
          aria-label="Field key"
          placeholder="key"
          :class="[inputClass, 'font-mono text-xs']"
          @input="patchField(i, { key: ($event.target as HTMLInputElement).value })"
        >
      </div>
      <input
        v-if="f.type === 'select'"
        :value="(f.options ?? []).join(', ')"
        placeholder="Choices, separated by commas"
        aria-label="Choices"
        :class="inputClass"
        @change="patchField(i, { options: ($event.target as HTMLInputElement).value.split(',').map((o) => o.trim()).filter((o) => o) })"
      >
      <div
        v-if="f.type === 'number'"
        class="grid grid-cols-2 gap-2"
      >
        <input
          :value="f.min ?? ''"
          type="number"
          placeholder="Minimum"
          aria-label="Minimum"
          :class="inputClass"
          @change="setNumber(i, 'min', ($event.target as HTMLInputElement).value)"
        >
        <input
          :value="f.max ?? ''"
          type="number"
          placeholder="Maximum"
          aria-label="Maximum"
          :class="inputClass"
          @change="setNumber(i, 'max', ($event.target as HTMLInputElement).value)"
        >
      </div>
      <label class="flex items-center gap-2 text-xs text-gray-300">
        <input
          type="checkbox"
          :checked="f.required === true"
          class="h-3.5 w-3.5 rounded border-gray-600 bg-gray-800"
          @change="patchField(i, { required: ($event.target as HTMLInputElement).checked })"
        >
        {{ f.type === 'checkbox' ? 'Must be ticked to approve' : 'Required' }}
      </label>
    </div>

    <ul
      v-if="problems.length > 0"
      class="space-y-1 text-xs text-amber-400"
      data-test="gate-form-problems"
    >
      <li
        v-for="p in problems"
        :key="p"
      >
        {{ p }}
      </li>
    </ul>

    <div class="flex flex-wrap gap-2">
      <button
        type="button"
        class="inline-flex items-center gap-1 rounded-md border border-gray-600 px-2 py-1 text-xs text-gray-200 hover:bg-gray-700 disabled:opacity-50"
        :disabled="fields.length >= MAX_FIELDS"
        data-test="gate-form-add"
        @click="addField"
      >
        <Plus class="h-3.5 w-3.5" /> Add field
      </button>
      <button
        v-if="workspaceId"
        type="button"
        class="inline-flex items-center gap-1 rounded-md border border-gray-600 px-2 py-1 text-xs text-gray-200 hover:bg-gray-700"
        data-test="gate-form-library"
        @click="openPicker"
      >
        <BookOpen class="h-3.5 w-3.5" /> Use a Library form
      </button>
      <button
        v-if="workspaceId && fields.length > 0 && problems.length === 0"
        type="button"
        class="inline-flex items-center gap-1 rounded-md border border-gray-600 px-2 py-1 text-xs text-gray-200 hover:bg-gray-700"
        data-test="gate-form-save"
        @click="saveOpen = !saveOpen"
      >
        <Save class="h-3.5 w-3.5" /> Save as a form
      </button>
    </div>

    <div
      v-if="pickerOpen"
      class="rounded-md border border-gray-700 bg-gray-900/60 p-2"
      data-test="gate-form-picker"
    >
      <p
        v-if="libraryLoading"
        class="text-xs text-gray-400"
      >
        Loading Library forms…
      </p>
      <p
        v-else-if="libraryError"
        class="text-xs text-amber-400"
      >
        {{ libraryError }}
      </p>
      <p
        v-else-if="libraryForms.length === 0"
        class="text-xs text-gray-400"
      >
        There are no Library forms yet. Build the fields here, then choose "Save as a form" to reuse them on other gates.
      </p>
      <ul
        v-else
        class="max-h-56 space-y-1 overflow-y-auto"
      >
        <li
          v-for="form in libraryForms"
          :key="form.id"
        >
          <button
            type="button"
            class="w-full rounded px-2 py-1.5 text-left hover:bg-gray-700"
            :data-test="`gate-form-use-${form.slug}`"
            @click="useForm(form)"
          >
            <span class="block text-sm text-gray-100">
              {{ form.name }}
              <span class="text-xs text-gray-500">· {{ form.isWorkspaceForm ? 'this workspace' : form.isOfficial ? 'Official' : form.publisher }}</span>
            </span>
            <span class="block text-xs text-gray-400">{{ form.fields.length }} fields · {{ form.fields.map((x) => x.label).join(', ') }}</span>
          </button>
        </li>
      </ul>
      <p
        v-if="fields.length > 0 && libraryForms.length > 0"
        class="mt-2 text-xs text-gray-500"
      >
        Choosing a form replaces the fields above.
      </p>
    </div>

    <form
      v-if="saveOpen"
      class="flex gap-2"
      @submit.prevent="saveAsForm"
    >
      <input
        v-model="saveName"
        placeholder="Form name, e.g. Vendor check"
        aria-label="Form name"
        :class="inputClass"
      >
      <button
        type="submit"
        class="rounded-md bg-indigo-600 px-3 py-1 text-xs font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
        :disabled="saving || saveName.trim() === ''"
      >
        {{ saving ? 'Saving…' : 'Save' }}
      </button>
    </form>
    <p
      v-if="saveMessage"
      :class="['text-xs', saveMessage.ok ? 'text-emerald-400' : 'text-amber-400']"
      role="status"
    >
      {{ saveMessage.text }}
    </p>
  </div>
</template>
