<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { FlaskConical, RefreshCw } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { fromNow } from '@/utils/date.util';
import { twinBadge, twinService, type TwinFinding, type TwinRun, type TwinSeverity } from '@/services/twin.service';

// Workflow Twin (FUNCTIONAL.md §8.1): the latest shadow simulation of this workflow — generated inputs (up to 5,000)
// run through its routers with nothing executed for real — and what broke.
const props = defineProps<{ workspaceId: string; workflowId: string; canRun: boolean }>();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const running = ref(false);
const run = ref<TwinRun | null>(null);
const automatic = ref(false);
const showInfo = ref(false);
/** True once the latest result has loaded; until then we don't know whether it's published, so we don't say. */
const loaded = ref(false);

const badge = computed(() => (run.value ? twinBadge(run.value.status, run.value.breakCount, run.value.warningCount) : null));
const visible = computed<TwinFinding[]>(() =>
  (run.value?.findings ?? []).filter((f) => showInfo.value || f.severity !== 'info'),
);
const infoCount = computed(() => (run.value?.findings ?? []).filter((f) => f.severity === 'info').length);

const severityStyle: Record<TwinSeverity, { label: string; variant: 'danger' | 'amber' | 'slate'; border: string }> = {
  break: { label: 'Break', variant: 'danger', border: 'border-danger-200' },
  warning: { label: 'Warning', variant: 'amber', border: 'border-amber-200' },
  info: { label: 'Info', variant: 'slate', border: 'border-slate-200' },
};

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  loaded.value = false;
  try {
    const latest = await twinService.latest(props.workspaceId, props.workflowId);
    run.value = latest.run;
    automatic.value = latest.hasProductionVersion;
    loaded.value = true;
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

async function runNow(): Promise<void> {
  running.value = true;
  try {
    run.value = await twinService.run(props.workspaceId, props.workflowId);
    const r = run.value;
    if (r.status === 'failed') toast.error(r.errorMessage ?? "The Twin couldn't simulate this workflow.");
    else toast.success(`Simulated ${r.casesRun.toLocaleString()} inputs: ${twinBadge(r.status, r.breakCount, r.warningCount).label.toLowerCase()}.`);
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    running.value = false;
  }
}

onMounted(load);
watch(() => props.workflowId, load);
</script>

<template>
  <div
    class="space-y-4"
    data-testid="twin-panel"
  >
    <div class="flex flex-wrap items-start justify-between gap-4 rounded-xl border border-slate-200 bg-white p-5">
      <div class="max-w-2xl">
        <h2 class="flex items-center gap-2 text-base font-semibold text-slate-900">
          <FlaskConical class="h-4 w-4 text-primary-600" />
          Workflow Twin
          <FmBadge
            v-if="badge"
            :variant="badge.variant"
            data-testid="twin-status"
          >
            {{ badge.label }}
          </FmBadge>
        </h2>
        <p class="mt-1 text-sm text-slate-500">
          Tries generated inputs against this workflow — boundary values, missing and badly formatted fields, recent
          real payloads, each approval approved and rejected — without running anything for real, and shows the ones
          that break it. The more decision points, the more inputs (up to 5,000).
          <template v-if="loaded && automatic">
            The published version is simulated automatically once a day, and within an hour of each publish.
          </template>
          <template v-else-if="loaded">
            This workflow isn't published, so the saved draft is simulated when you run it.
          </template>
        </p>
        <p
          v-if="run"
          class="mt-2 text-xs text-slate-500"
          data-testid="twin-meta"
        >
          Last run {{ fromNow(run.completedAt) }} ({{ run.trigger === 'scheduled' ? 'automatic' : 'run by hand' }}) on the
          {{ run.versionLabel === 'production' ? 'published version' : 'saved draft' }}:
          {{ run.casesRun.toLocaleString() }} {{ run.casesRun === 1 ? 'input' : 'inputs' }},
          {{ run.routersTested }} {{ run.routersTested === 1 ? 'decision point' : 'decision points' }}, {{ run.durationMs }} ms.
        </p>
      </div>
      <FmButton
        v-if="canRun && loaded"
        variant="secondary"
        :loading="running"
        data-testid="twin-run"
        @click="runNow"
      >
        <template #icon-left>
          <RefreshCw class="h-4 w-4" />
        </template>
        {{ run ? 'Run again' : 'Run the Twin' }}
      </FmButton>
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-12 text-primary-600"
      data-testid="twin-loading"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load the Twin result"
      :description="loadError"
      action-label="Try again"
      help-article="workflows/workflow-twin"
      @action="load"
    />

    <div
      v-else-if="!run"
      data-testid="twin-empty"
    >
      <FmEmptyState
        :icon="FlaskConical"
        title="Not simulated yet"
        :description="canRun
          ? 'Run the Twin to find the inputs that break this workflow before real users do. It takes about a second and runs nothing for real.'
          : 'A Designer or Admin can run the Twin here. Published workflows are also simulated automatically once a day.'"
        :cta-label="canRun ? 'Run the Twin' : undefined"
        @cta="runNow"
      />
    </div>

    <FmAlert
      v-else-if="run.status === 'failed'"
      type="error"
      data-testid="twin-failed"
    >
      {{ run.errorMessage }}
    </FmAlert>

    <template v-else>
      <div
        v-if="visible.length === 0"
        class="rounded-xl border border-primary-200 bg-primary-50 p-5 text-sm text-primary-800"
        data-testid="twin-clean"
      >
        No input broke this workflow: every one of the {{ run.casesRun.toLocaleString() }} simulated runs found a route at
        every decision point. The Twin checks routing only — it doesn't call your connectors, so it can't tell whether a
        step's own settings are right.
      </div>

      <article
        v-for="(f, i) in visible"
        :key="`${f.kind}-${f.nodeId}-${i}`"
        :class="['rounded-xl border bg-white p-5', severityStyle[f.severity].border]"
        data-testid="twin-finding"
      >
        <div class="flex flex-wrap items-center gap-2">
          <FmBadge :variant="severityStyle[f.severity].variant">
            {{ severityStyle[f.severity].label }}
          </FmBadge>
          <h3 class="font-medium text-slate-900">
            {{ f.title }}
          </h3>
          <span class="text-xs text-slate-500">at “{{ f.nodeLabel }}”</span>
          <FmBadge
            v-if="f.fromRealPayload"
            variant="blue"
          >
            Seen in a real run's input
          </FmBadge>
          <span
            v-if="f.cases > 0"
            class="ml-auto text-xs text-slate-400"
          >{{ f.cases.toLocaleString() }} {{ f.cases === 1 ? 'input' : 'inputs' }}</span>
        </div>
        <p class="mt-2 text-sm text-slate-600">
          {{ f.explanation }}
        </p>
        <p class="mt-2 text-sm text-slate-800">
          <span class="font-medium">Fix:</span> {{ f.fix }}
        </p>
        <dl
          v-if="Object.keys(f.example).length > 0"
          class="mt-3 flex flex-wrap gap-2 text-xs"
        >
          <div
            v-for="(value, key) in f.example"
            :key="key"
            class="rounded bg-slate-50 px-2 py-1 font-mono text-slate-700"
          >
            <dt class="inline text-slate-400">
              {{ key }}:
            </dt>
            <dd class="inline">
              {{ value }}
            </dd>
          </div>
        </dl>
        <p
          v-if="f.alsoFailsWith.length > 0"
          class="mt-2 text-xs text-slate-500"
        >
          Also: {{ f.alsoFailsWith.join(' · ') }}
        </p>
      </article>

      <button
        v-if="infoCount > 0"
        type="button"
        class="text-sm font-medium text-slate-500 hover:text-slate-700"
        data-testid="twin-toggle-info"
        @click="showInfo = !showInfo"
      >
        {{ showInfo ? 'Hide' : 'Show' }} {{ infoCount }} {{ infoCount === 1 ? 'note' : 'notes' }} about branches nothing took
      </button>
    </template>
  </div>
</template>
