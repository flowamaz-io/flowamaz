<script setup lang="ts">
import { computed, ref } from 'vue';
import FmBadge from './FmBadge.vue';
import FmButton from './FmButton.vue';
import FmRunTimeline from './FmRunTimeline.vue';
import VariableInspector from './VariableInspector.vue';
import { fromNow } from '../date';
import { canCancelRun, instanceStatusVariant, isRunEnded, runDuration } from '../runs';
import type { InstanceDetailResponse, TimelineResponse } from '../types';

// One run: header (status, duration, live state, cancel / retry), then tabs for the timeline, steps, variables and
// events. Hosts add their own buttons (#actions), content under the timeline (#timeline-extra) and extra tabs
// (extraTabs + #tab-<name>).
const props = withDefaults(
  defineProps<{
    run: InstanceDetailResponse;
    timeline: TimelineResponse | null;
    /** Whether the viewer may cancel and retry. */
    canAct?: boolean;
    /** Disables the buttons while a cancel or retry is in flight. */
    acting?: boolean;
    /** The live-update state to show next to the status; null shows nothing. */
    liveState?: 'live' | 'reconnecting' | null;
    /** More tabs after the built-in ones, each filled by the slot `tab-<name>`. */
    extraTabs?: string[];
  }>(),
  { canAct: false, acting: false, liveState: null, extraTabs: () => [] },
);

defineEmits<{ cancel: []; retry: [] }>();

const dataHidden = computed(() => props.run.dataHidden === true);
const ended = computed(() => isRunEnded(props.run.status));
const tabs = computed<string[]>(() => [
  ...['timeline', 'nodes', 'variables', 'events'].filter((t) => !(t === 'variables' && dataHidden.value)),
  ...props.extraTabs,
]);
const activeTab = ref('timeline');
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-start justify-between">
      <div>
        <slot name="back" />
        <h1 class="mt-1 font-mono text-lg font-semibold text-slate-900">
          {{ run.id.slice(0, 8) }}
        </h1>
        <div class="mt-2 flex items-center gap-3 text-sm">
          <FmBadge :variant="instanceStatusVariant(run.status)">
            {{ run.status }}
          </FmBadge>
          <span class="text-slate-500">{{ runDuration(run) }}</span>
          <span
            v-if="!ended && liveState === 'live'"
            class="text-xs text-teal-600"
          >● Live</span>
          <span
            v-else-if="!ended && liveState === 'reconnecting'"
            class="text-xs text-amber-600"
          >Reconnecting…</span>
        </div>
      </div>
      <div class="flex gap-2">
        <FmButton
          v-if="canAct && canCancelRun(run.status)"
          variant="secondary"
          :loading="acting"
          @click="$emit('cancel')"
        >
          Cancel
        </FmButton>
        <FmButton
          v-if="canAct && run.status === 'Failed'"
          :loading="acting"
          @click="$emit('retry')"
        >
          Retry
        </FmButton>
        <slot name="actions" />
      </div>
    </div>

    <p
      v-if="dataHidden"
      class="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600"
      data-testid="run-data-hidden"
    >
      <slot name="data-hidden">
        You can see this run's steps, status and errors. Its data (inputs, outputs and variables) is hidden for your role.
      </slot>
    </p>

    <div class="flex gap-1 border-b border-slate-200">
      <button
        v-for="tab in tabs"
        :key="tab"
        type="button"
        :class="[
          '-mb-px border-b-2 px-4 py-2 text-sm font-medium capitalize',
          activeTab === tab ? 'border-primary-600 text-primary-700' : 'border-transparent text-slate-500 hover:text-slate-700',
        ]"
        @click="activeTab = tab"
      >
        {{ tab }}
      </button>
    </div>

    <div
      v-if="activeTab === 'timeline'"
      class="space-y-4"
    >
      <div class="rounded-xl border border-slate-200 bg-white p-5">
        <FmRunTimeline
          v-if="timeline && timeline.nodes.length > 0"
          :timeline="timeline"
        />
        <p
          v-else
          class="text-sm text-slate-400"
        >
          No node activity yet. Trigger or replay this workflow to populate the timeline.
        </p>
      </div>
      <slot name="timeline-extra" />
    </div>

    <div
      v-else-if="activeTab === 'nodes'"
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <thead class="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
          <tr>
            <th class="px-4 py-3">
              Node
            </th>
            <th class="px-4 py-3">
              Status
            </th>
            <th class="px-4 py-3">
              Retries
            </th>
            <th class="px-4 py-3">
              Error
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="node in run.nodeStates"
            :key="node.nodeId"
          >
            <td class="px-4 py-3">
              <span class="font-medium text-slate-800">{{ node.nodeId }}</span>
              <span class="ml-2 text-xs text-slate-400">{{ node.nodeType }}</span>
            </td>
            <td class="px-4 py-3 text-slate-600">
              {{ node.status }}
            </td>
            <td class="px-4 py-3 tabular-nums text-slate-600">
              {{ node.retryCount }}
            </td>
            <td class="px-4 py-3 text-xs text-danger-600">
              {{ node.errorMessage ?? '—' }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-else-if="activeTab === 'variables' && !dataHidden">
      <VariableInspector
        :instance-id="run.id"
        :status="run.status"
      />
    </div>

    <div
      v-else-if="activeTab === 'events'"
      class="space-y-2"
    >
      <div
        v-for="event in run.events"
        :key="event.sequenceNumber"
        class="rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm"
      >
        <div class="flex items-center justify-between">
          <span class="font-medium text-slate-800">{{ event.eventType }}</span>
          <span class="text-xs text-slate-400">{{ fromNow(event.occurredAt) }}</span>
        </div>
        <p class="text-xs text-slate-500">
          {{ event.nodeId ?? '—' }} · {{ dataHidden ? '(data hidden)' : event.payload }}
        </p>
      </div>
    </div>

    <template
      v-for="tab in extraTabs"
      :key="tab"
    >
      <div v-if="activeTab === tab">
        <slot :name="`tab-${tab}`" />
      </div>
    </template>
  </div>
</template>
