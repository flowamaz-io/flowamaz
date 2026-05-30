<script setup lang="ts">
import { ref, watch } from 'vue';
import { empathyService, type EmpathyAnalysis, type EmpathyIssue } from '../../services/empathy.service';
import FmTooltip from '@/components/common/FmTooltip.vue';

interface Props {
  workspaceId: string;
  workflowId: string;
  visible: boolean;
  yamlContent?: string;
}

const props = defineProps<Props>();

const loading = ref(false);
const error = ref<string | null>(null);
const analysis = ref<EmpathyAnalysis | null>(null);
const expandedIssues = ref<Set<number>>(new Set());

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  analysis.value = null;
  try {
    analysis.value = await empathyService.getEmpathyAnalysis(
      props.workspaceId,
      props.workflowId,
      props.yamlContent,
    );
  } catch (e: unknown) {
    error.value = `Failed to load empathy analysis. ${e instanceof Error ? e.message : 'Please try again.'}`;
  } finally {
    loading.value = false;
  }
}

watch(() => props.visible, (val) => {
  if (val && analysis.value === null && !loading.value) {
    load();
  }
}, { immediate: true });

function toggleIssue(index: number): void {
  if (expandedIssues.value.has(index)) {
    expandedIssues.value.delete(index);
  } else {
    expandedIssues.value.add(index);
  }
}

function scoreColor(score: number): string {
  if (score >= 80) return 'text-emerald-600';
  if (score >= 50) return 'text-amber-600';
  return 'text-red-600';
}

function scoreBg(score: number): string {
  if (score >= 80) return 'bg-emerald-50 border-emerald-300';
  if (score >= 50) return 'bg-amber-50 border-amber-300';
  return 'bg-red-50 border-red-300';
}

function severityClasses(severity: EmpathyIssue['severity']): string {
  if (severity === 'High') return 'bg-red-100 text-red-700 border border-red-300';
  if (severity === 'Medium') return 'bg-amber-100 text-amber-700 border border-amber-300';
  return 'bg-gray-100 text-gray-600 border border-gray-300';
}

function issueTypeLabel(type: EmpathyIssue['type']): string {
  const map: Record<EmpathyIssue['type'], string> = {
    NoStatusUpdate: 'No Status Update',
    LongWait: 'Long Wait',
    MultipleEmails: 'Too Many Emails',
    NoOutcomeNotification: 'No Outcome Notification',
    VisibilityGap: 'Visibility Gap',
  };
  return map[type] ?? type;
}
</script>

<template>
  <div class="flex flex-col h-full overflow-hidden bg-white border-l border-gray-200">
    <!-- Header -->
    <div class="flex items-center justify-between px-4 py-3 border-b border-gray-200 bg-gray-50 shrink-0">
      <span class="text-sm font-semibold text-gray-900">Workflow Empathy</span>
      <button
        class="text-xs text-teal-600 hover:text-teal-700"
        @click="load"
      >
        Refresh
      </button>
    </div>

    <!-- Loading skeleton -->
    <div
      v-if="loading"
      class="flex-1 p-4 space-y-3 overflow-y-auto"
    >
      <div class="h-20 bg-gray-100 rounded-lg animate-pulse" />
      <div class="h-12 bg-gray-100 rounded-lg animate-pulse" />
      <div class="h-12 bg-gray-100 rounded-lg animate-pulse" />
    </div>

    <!-- Error state -->
    <div
      v-else-if="error"
      class="flex-1 flex flex-col items-center justify-center p-6 text-center gap-3"
    >
      <span class="text-red-600 text-sm">{{ error }}</span>
      <button
        class="text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded-lg px-3 py-1.5"
        @click="load"
      >
        Try again
      </button>
    </div>

    <!-- Content -->
    <div
      v-else-if="analysis"
      class="flex-1 overflow-y-auto p-4 space-y-4"
    >
      <!-- Score -->
      <div :class="['flex items-center justify-between rounded-lg border px-4 py-3', scoreBg(analysis.score)]">
        <span class="flex items-center gap-1 text-xs text-gray-500 uppercase tracking-wider font-medium">
          Empathy Score
          <FmTooltip
            text="Empathy score measures how human-friendly this workflow is for the people in it."
            label="About empathy score"
          />
        </span>
        <span :class="['text-3xl font-bold', scoreColor(analysis.score)]">{{ analysis.score }}</span>
      </div>

      <!-- Summary metrics -->
      <div class="grid grid-cols-2 gap-2">
        <div class="bg-gray-50 rounded-lg px-3 py-2 border border-gray-200">
          <div class="text-xs text-gray-500">
            Emails sent
          </div>
          <div class="text-lg font-semibold text-gray-900">
            {{ analysis.emailsSentToRequester }}
          </div>
        </div>
        <div class="bg-gray-50 rounded-lg px-3 py-2 border border-gray-200">
          <div class="text-xs text-gray-500">
            Wait periods
          </div>
          <div class="text-lg font-semibold text-gray-900">
            {{ analysis.waitPeriodCount }}
          </div>
        </div>
        <div class="bg-gray-50 rounded-lg px-3 py-2 border border-gray-200">
          <div class="text-xs text-gray-500">
            Status updates
          </div>
          <div class="text-lg font-semibold text-gray-900">
            {{ analysis.statusUpdateCount }}
          </div>
        </div>
        <div class="bg-gray-50 rounded-lg px-3 py-2 border border-gray-200">
          <div class="text-xs text-gray-500">
            Avg days to outcome
          </div>
          <div class="text-lg font-semibold text-gray-900">
            {{ analysis.avgDaysToOutcome }}
          </div>
        </div>
      </div>

      <!-- Empty state -->
      <div
        v-if="analysis.issues.length === 0"
        class="rounded-lg bg-emerald-50 border border-emerald-200 px-4 py-5 text-center"
      >
        <div class="text-emerald-700 text-sm font-medium">
          Great workflow experience
        </div>
        <div class="text-emerald-600 text-xs mt-1">
          No empathy issues found. Your workflow is considerate of the people involved.
        </div>
      </div>

      <!-- Issues list -->
      <div
        v-else
        class="space-y-2"
      >
        <div class="text-xs text-gray-500 uppercase tracking-wider font-medium">
          Issues ({{ analysis.issues.length }})
        </div>
        <div
          v-for="(issue, index) in analysis.issues"
          :key="index"
          class="bg-white rounded-lg border border-gray-200 overflow-hidden"
        >
          <button
            class="w-full flex items-start gap-3 px-4 py-3 text-left hover:bg-gray-50 transition-colors"
            @click="toggleIssue(index)"
          >
            <span :class="['text-xs px-2 py-0.5 rounded-full shrink-0 mt-0.5', severityClasses(issue.severity)]">
              {{ issue.severity }}
            </span>
            <div class="flex-1 min-w-0">
              <div class="text-sm font-medium text-gray-900 truncate">
                {{ issue.label }}
              </div>
              <div class="text-xs text-gray-500 truncate">
                {{ issueTypeLabel(issue.type) }}
              </div>
            </div>
            <span class="text-gray-400 text-xs shrink-0">{{ expandedIssues.has(index) ? '▲' : '▼' }}</span>
          </button>

          <div
            v-if="expandedIssues.has(index)"
            class="px-4 pb-4 space-y-3 border-t border-gray-200"
          >
            <p class="text-xs text-gray-600 leading-relaxed pt-3">
              {{ issue.description }}
            </p>
            <div class="bg-violet-50 border border-violet-200 rounded-lg px-3 py-2">
              <div class="text-xs text-violet-700 font-medium mb-1">
                How to fix
              </div>
              <p class="text-xs text-violet-800 leading-relaxed">
                {{ issue.suggestion }}
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Not yet loaded -->
    <div
      v-else
      class="flex-1 flex items-center justify-center"
    >
      <span class="text-gray-400 text-sm">Toggle Empathy mode to analyse your workflow.</span>
    </div>
  </div>
</template>
