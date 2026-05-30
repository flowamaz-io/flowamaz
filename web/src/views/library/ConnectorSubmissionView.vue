<script setup lang="ts">
import { computed, ref } from 'vue';
import { ArrowLeft, UploadCloud, CheckCircle2 } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { useToast } from '@/composables/useToast';
import { useRouter } from 'vue-router';
import { toUserFacingError } from '@/utils/error.util';
import { connectorService, type ConnectorSubmissionResult } from '@/services/connector.service';

const ws = useWorkspace();
const toast = useToast();
const router = useRouter();

const name = ref('');
const description = ref('');
const category = ref('generic');
const manifestYaml = ref('');
const submitting = ref(false);
const result = ref<ConnectorSubmissionResult | null>(null);

const CATEGORIES = ['generic', 'database', 'messaging', 'devops', 'productivity', 'hr', 'engineering', 'ai'];

const canSubmit = computed(() => name.value.trim().length > 0 && manifestYaml.value.trim().length > 0);

function onFileChange(event: Event): void {
  const file = (event.target as HTMLInputElement).files?.[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = () => { manifestYaml.value = String(reader.result ?? ''); };
  reader.readAsText(file);
}

async function submit(): Promise<void> {
  const workspaceId = ws.currentWorkspaceId.value;
  if (!workspaceId || !canSubmit.value) return;
  submitting.value = true;
  try {
    result.value = await connectorService.submitConnector(workspaceId, name.value.trim(), manifestYaml.value);
    toast.success('Connector submitted — a pull request was opened for review.');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="mx-auto max-w-2xl space-y-6 p-6">
    <button
      class="flex items-center gap-1.5 text-sm text-slate-500 transition-colors hover:text-slate-800"
      @click="router.push('/library')"
    >
      <ArrowLeft class="h-4 w-4" />
      Back to Library
    </button>

    <header>
      <h1 class="text-2xl font-semibold text-slate-900">
        Submit a connector
      </h1>
      <p class="text-sm text-slate-500">
        Share a community connector. Your manifest is validated and a pull request is opened against
        the connectors repository for review.
      </p>
    </header>

    <FmAlert
      v-if="result"
      type="success"
    >
      <p class="flex items-center gap-2 font-medium">
        <CheckCircle2 class="h-4 w-4" />
        Submission received — status: {{ result.status }}
      </p>
      <p class="mt-1 text-sm">
        Track your review here:
        <a
          :href="result.githubPrUrl"
          target="_blank"
          rel="noopener noreferrer"
          class="font-medium text-primary-600 underline"
        >{{ result.githubPrUrl }}</a>
      </p>
    </FmAlert>

    <section
      v-else
      class="space-y-4 rounded-xl border border-slate-200 bg-white p-6"
    >
      <FmInput
        v-model="name"
        label="Connector name"
        placeholder="e.g. Acme CRM"
      />
      <FmInput
        v-model="description"
        label="Short description"
        placeholder="What does this connector do?"
      />
      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">Category</label>
        <select
          v-model="category"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm capitalize focus:border-primary-500 focus:outline-none"
        >
          <option
            v-for="c in CATEGORIES"
            :key="c"
            :value="c"
          >
            {{ c }}
          </option>
        </select>
      </div>
      <div>
        <div class="mb-1 flex items-center justify-between">
          <label class="block text-sm font-medium text-slate-700">Manifest YAML</label>
          <label class="flex cursor-pointer items-center gap-1 text-xs text-primary-600 hover:text-primary-700">
            <UploadCloud class="h-3.5 w-3.5" />
            Upload file
            <input
              type="file"
              accept=".yaml,.yml"
              class="hidden"
              @change="onFileChange"
            >
          </label>
        </div>
        <textarea
          v-model="manifestYaml"
          rows="10"
          placeholder="id: acme-crm&#10;version: 1.0.0&#10;operations: []"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 font-mono text-xs focus:border-primary-500 focus:outline-none"
        />
      </div>
      <div class="flex justify-end">
        <FmButton
          :loading="submitting"
          :disabled="!canSubmit"
          @click="submit"
        >
          Submit for review
        </FmButton>
      </div>
    </section>
  </div>
</template>
