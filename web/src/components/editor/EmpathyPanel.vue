<script setup lang="ts">
import { ref, watch } from 'vue';
import { empathyService, type EmpathyAnalysis, type EmpathyIssue } from '../../services/empathy.service';

interface Props {
  workspaceId: string;
  workflowId: string;
  visible: boolean;
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
    analysis.value = await empathyService.getEmpathyAnalysis(props.workspaceId, props.workflowId);
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
  if (score >= 80) return 'text-emerald-400';
  if (score >= 50) return 'text-amber-400';
  return 'text-red-400';
}

function scoreBg(score: number): string {
  if (score >= 80) return 'bg-emerald-900/30 border-emerald-700';
  if (score >= 50) return 'bg-amber-900/30 border-amber-700';
  return 'bg-red-900/30 border-red-700';
}

function severityClasses(severity: EmpathyIssue['severity']): string {
  if (severity === 'High') return 'bg-red-900/50 text-red-300 border border-red-700';
  if (severity === 'Medium') return 'bg-amber-900/50 text-amber-300 border border-amber-700';
  return 'bg-neutral-700 text-neutral-300 border border-neutral-600';
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
  <div class="flex flex-col h-full overflow-hidden bg-neutral-900 border-l border-neutral-700">
    <!-- Header -->
    <div class="flex items-center justify-between px-4 py-3 border-b border-neutral-700 shrink-0">
      <span class="text-sm font-semibold text-white">Workflow Empathy</span>
      <button
        class="text-xs text-violet-400 hover:text-violet-300"
        @click="load"
      >Refresh</button>
    </div>

    <!-- Loading skeleton -->
    <div v-if="loading" class="flex-1 p-4 space-y-3 overflow-y-auto">
      <div class="h-20 bg-neutral-800 rounded-lg animate-pulse" />
      <div class="h-12 bg-neutral-800 rounded-lg animate-pulse" />
      <div class="h-12 bg-neutral-800 rounded-lg animate-pulse" />
    </div>

    <!-- Error state -->
    <div v-else-if="error" class="flex-1 flex flex-col items-center justify-center p-6 text-center gap-3">
      <span class="text-red-400 text-sm">{{ error }}</span>
      <button
        class="text-xs bg-neutral-700 hover:bg-neutral-600 text-white rounded-lg px-3 py-1.5"
        @click="load"
      >Try again</button>
    </div>

    <!-- Content -->
    <div v-else-if="analysis" class="flex-1 overflow-y-auto p-4 space-y-4">
      <!-- Score -->
      <div :class="['flex items-center justify-between rounded-lg border px-4 py-3', scoreBg(analysis.score)]">
        <span class="text-xs text-neutral-400 uppercase tracking-wider font-medium">Empathy Score</span>
        <span :class="['text-3xl font-bold', scoreColor(analysis.score)]">{{ analysis.score }}</span>
      </div>

      <!-- Summary metrics -->
      <div class="grid grid-cols-2 gap-2">
        <div class="bg-neutral-800 rounded-lg px-3 py-2">
          <div class="text-xs text-neutral-400">Emails sent</div>
          <div class="text-lg font-semibold text-white">{{ analysis.emailsSentToRequester }}</div>
        </div>
        <div class="bg-neutral-800 rounded-lg px-3 py-2">
          <div class="text-xs text-neutral-400">Wait periods</div>
          <div class="text-lg font-semibold text-white">{{ analysis.waitPeriodCount }}</div>
        </div>
        <div class="bg-neutral-800 rounded-lg px-3 py-2">
          <div class="text-xs text-neutral-400">Status updates</div>
          <div class="text-lg font-semibold text-white">{{ analysis.statusUpdateCount }}</div>
        </div>
        <div class="bg-neutral-800 rounded-lg px-3 py-2">
          <div class="text-xs text-neutral-400">Avg days to outcome</div>
          <div class="text-lg font-semibold text-white">{{ analysis.avgDaysToOutcome }}</div>
        </div>
      </div>

      <!-- Empty state -->
      <div
        v-if="analysis.issues.length === 0"
        class="rounded-lg bg-emerald-900/20 border border-emerald-700 px-4 py-5 text-center"
      >
        <div class="text-emerald-400 text-sm font-medium">Great workflow experience</div>
        <div class="text-emerald-600 text-xs mt-1">No empathy issues found. Your workflow is considerate of the people involved.</div>
      </div>

      <!-- Issues list -->
      <div v-else class="space-y-2">
        <div class="text-xs text-neutral-400 uppercase tracking-wider font-medium">Issues ({{ analysis.issues.length }})</div>
        <div
          v-for="(issue, index) in analysis.issues"
          :key="index"
          class="bg-neutral-800 rounded-lg overflow-hidden"
        >
          <button
            class="w-full flex items-start gap-3 px-4 py-3 text-left hover:bg-neutral-700/50 transition-colors"
            @click="toggleIssue(index)"
          >
            <span :class="['text-xs px-2 py-0.5 rounded-full shrink-0 mt-0.5', severityClasses(issue.severity)]">
              {{ issue.severity }}
            </span>
            <div class="flex-1 min-w-0">
              <div class="text-sm font-medium text-white truncate">{{ issue.label }}</div>
              <div class="text-xs text-neutral-400 truncate">{{ issueTypeLabel(issue.type) }}</div>
            </div>
            <span class="text-neutral-500 text-xs shrink-0">{{ expandedIssues.has(index) ? '▲' : '▼' }}</span>
          </button>

          <div v-if="expandedIssues.has(index)" class="px-4 pb-4 space-y-3 border-t border-neutral-700">
            <p class="text-xs text-neutral-300 leading-relaxed pt-3">{{ issue.description }}</p>
            <div class="bg-violet-900/30 border border-violet-700 rounded-lg px-3 py-2">
              <div class="text-xs text-violet-400 font-medium mb-1">How to fix</div>
              <p class="text-xs text-violet-200 leading-relaxed">{{ issue.suggestion }}</p>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Not yet loaded (shouldn't happen — loaded on visible=true) -->
    <div v-else class="flex-1 flex items-center justify-center">
      <span class="text-neutral-500 text-sm">Toggle Empathy mode to analyse your workflow.</span>
    </div>
  </div>
</template>
