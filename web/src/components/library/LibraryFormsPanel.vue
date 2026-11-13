<script setup lang="ts">
import { toUserFacingError } from '@/utils/error.util';
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { ClipboardList, Search, Trash2 } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import { libraryService, type LibraryFormItem } from '@/services/library.service';
import { useWorkspace } from '@/composables/useWorkspace';
import { canDesign } from '@/utils/constants';
import { FIELD_TYPES } from '@/utils/gateForm.util';

// Library → Forms (FUNCTIONAL.md §23.2): forms approvers fill in at a human gate. Used from the gate's
// "Use a Library form"; this tab is for browsing them and removing this workspace's own.
const router = useRouter();
const { currentWorkspaceId, current, loadWorkspaces } = useWorkspace();
const canEdit = computed(() => canDesign(current.value?.role));

const forms = ref<LibraryFormItem[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);
const query = ref('');
const expanded = ref<string | null>(null);
const deleting = ref<string | null>(null);
/** The form whose delete is waiting for "Yes, delete". */
const confirming = ref<string | null>(null);

const typeLabel = (t: string): string => FIELD_TYPES.find((x) => x.value === t)?.label ?? t;

const shown = computed(() => {
  const words = query.value.toLowerCase().split(/\s+/).filter((w) => w);
  return forms.value.filter((f) => words.every((w) =>
    f.name.toLowerCase().includes(w) || f.category.toLowerCase().includes(w) || f.fields.some((x) => x.label.toLowerCase().includes(w))));
});

async function load(): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  if (!currentWorkspaceId.value) return;
  loading.value = true;
  error.value = null;
  try {
    forms.value = await libraryService.listForms(currentWorkspaceId.value);
  } catch (err: unknown) {
    error.value = toUserFacingError(err).message || 'Forms could not be loaded. Check your connection, then select "Try again".';
  } finally {
    loading.value = false;
  }
}

async function remove(form: LibraryFormItem): Promise<void> {
  if (!currentWorkspaceId.value) return;
  confirming.value = null;
  deleting.value = form.id;
  try {
    await libraryService.deleteForm(currentWorkspaceId.value, form.id);
    forms.value = forms.value.filter((f) => f.id !== form.id);
  } catch (err: unknown) {
    error.value = toUserFacingError(err).message || 'The form was not deleted. Reload the page and try again.';
  } finally {
    deleting.value = null;
  }
}

function openWorkflows(): void {
  void router.push({ name: 'workflows' }).catch(() => undefined);
}

onMounted(load);
</script>

<template>
  <div
    class="space-y-4"
    data-testid="library-forms"
  >
    <p class="text-sm text-slate-600">
      Forms collect details from the approver at a human gate — an amount, a cost centre, a date. To use one,
      open a workflow, select a human gate and choose <span class="font-medium">Use a Library form</span>.
    </p>
    <div class="relative w-full max-w-xs">
      <Search class="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
      <input
        v-model="query"
        type="text"
        placeholder="Search forms..."
        class="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
      >
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>
    <FmErrorState
      v-else-if="error"
      title="Couldn't load forms"
      :description="error"
      action-label="Try again"
      @action="load"
    />
    <FmEmptyState
      v-else-if="shown.length === 0"
      :icon="ClipboardList"
      :title="forms.length === 0 ? 'No forms yet' : 'No forms match'"
      :description="forms.length === 0
        ? 'Build the fields on a human gate, then choose Save as a form to reuse them on other gates.'
        : `Nothing matches “${query}”. Try a shorter word, or clear the search.`"
      :cta-label="forms.length === 0 ? 'Open workflows' : 'Clear search'"
      @cta="forms.length === 0 ? openWorkflows() : (query = '')"
    />
    <div
      v-else
      class="grid gap-3 md:grid-cols-2"
    >
      <article
        v-for="form in shown"
        :key="form.id"
        class="rounded-xl border border-slate-200 bg-white p-4"
        :data-testid="`library-form-${form.slug}`"
      >
        <div class="flex items-start justify-between gap-2">
          <div>
            <h3 class="font-medium text-slate-900">
              {{ form.name }}
            </h3>
            <p class="text-xs text-slate-500">
              {{ form.category }} · {{ form.fields.length }} {{ form.fields.length === 1 ? 'field' : 'fields' }}
            </p>
          </div>
          <router-link
            v-if="form.publisher && !form.isWorkspaceForm"
            :to="`/library/publishers/${form.publisher}`"
            class="shrink-0"
            :data-testid="`form-publisher-link`"
          >
            <FmBadge :variant="form.isWorkspaceForm ? 'blue' : form.isOfficial ? 'primary' : 'slate'">
              {{ form.isWorkspaceForm ? 'This workspace' : form.isOfficial ? 'Official' : form.publisher }}
            </FmBadge>
          </router-link>
          <FmBadge
            v-else
            :variant="form.isWorkspaceForm ? 'blue' : form.isOfficial ? 'primary' : 'slate'"
          >
            {{ form.isWorkspaceForm ? 'This workspace' : form.isOfficial ? 'Official' : form.publisher }}
          </FmBadge>
        </div>
        <p
          v-if="form.description"
          class="mt-2 text-sm text-slate-600"
        >
          {{ form.description }}
        </p>
        <p
          v-if="form.deprecatedAt"
          class="mt-2 text-xs text-amber-700"
        >
          Deprecated: {{ form.deprecationNote ?? 'its publisher no longer maintains it.' }} Gates that already use it keep their copy.
        </p>
        <button
          type="button"
          class="mt-3 text-xs font-medium text-indigo-600 hover:text-indigo-800"
          @click="expanded = expanded === form.id ? null : form.id"
        >
          {{ expanded === form.id ? 'Hide fields' : 'Show fields' }}
        </button>
        <ul
          v-if="expanded === form.id"
          class="mt-2 space-y-1 text-sm"
        >
          <li
            v-for="f in form.fields"
            :key="f.key"
            class="flex justify-between gap-2"
          >
            <span class="text-slate-800">{{ f.label }}<span
              v-if="f.required"
              class="text-red-600"
            > *</span></span>
            <span class="text-xs text-slate-500">{{ typeLabel(f.type) }}<template v-if="f.options?.length">: {{ f.options.join(', ') }}</template></span>
          </li>
        </ul>
        <div
          v-if="form.isWorkspaceForm && canEdit"
          class="mt-3 border-t border-slate-100 pt-3"
        >
          <div
            v-if="confirming === form.id"
            class="flex flex-wrap items-center gap-2 text-xs"
            :data-testid="`library-form-confirm-${form.slug}`"
          >
            <span class="text-slate-700">Delete “{{ form.name }}”?</span>
            <button
              type="button"
              class="rounded bg-red-600 px-2 py-1 font-medium text-white hover:bg-red-700"
              @click="remove(form)"
            >
              Yes, delete
            </button>
            <button
              type="button"
              class="rounded px-2 py-1 text-slate-600 hover:bg-slate-100"
              @click="confirming = null"
            >
              Cancel
            </button>
          </div>
          <button
            v-else
            type="button"
            class="inline-flex items-center gap-1 text-xs text-slate-500 hover:text-red-600 disabled:opacity-50"
            :disabled="deleting === form.id"
            @click="confirming = form.id"
          >
            <Trash2 class="h-3.5 w-3.5" />
            {{ deleting === form.id ? 'Deleting…' : 'Delete form' }}
          </button>
          <p class="mt-1 text-xs text-slate-400">
            Gates that use it keep their own copy of the fields.
          </p>
        </div>
      </article>
    </div>
  </div>
</template>
