<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import dayjs from 'dayjs';
import { ScrollText, Download, ChevronDown, ChevronRight } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { auditService, type AuditRow, type AuditPagination } from '@/services/audit.service';

const ws = useWorkspace();
const toast = useToast();

const workspaceId = computed(() => ws.currentWorkspaceId.value ?? '');

// Filters — default to the last 7 days.
const fromDate = ref(dayjs().subtract(7, 'day').format('YYYY-MM-DD'));
const toDate = ref(dayjs().format('YYYY-MM-DD'));
const eventType = ref('');
const actorId = ref('');
const resourceType = ref('');
const page = ref(1);
const pageSize = ref(20);

const loading = ref(true);
const loadError = ref('');
const exporting = ref(false);
const rows = ref<AuditRow[]>([]);
const pagination = ref<AuditPagination | null>(null);
const expanded = ref<Set<string>>(new Set());

// Curated common event types (free-text actor/resource still filter server-side).
const eventTypeOptions = [
  { value: '', label: 'All events' },
  { value: 'workflow.created', label: 'Workflow created' },
  { value: 'workflow.published', label: 'Workflow published' },
  { value: 'instance.started', label: 'Instance started' },
  { value: 'gate.approved', label: 'Gate approved' },
  { value: 'gate.rejected', label: 'Gate rejected' },
  { value: 'member.invited', label: 'Member invited' },
  { value: 'api_key.created', label: 'API key created' },
  { value: 'api_key.revoked', label: 'API key revoked' },
  { value: 'webhook.created', label: 'Webhook created' },
  { value: 'sso.login_succeeded', label: 'SSO login' },
  { value: 'billing.plan_upgraded', label: 'Plan upgraded' },
];

const resourceTypeOptions = [
  { value: '', label: 'All resources' },
  { value: 'workflow', label: 'Workflow' },
  { value: 'instance', label: 'Instance' },
  { value: 'member', label: 'Member' },
  { value: 'gate', label: 'Gate' },
  { value: 'api_key', label: 'API key' },
  { value: 'webhook', label: 'Webhook' },
  { value: 'sso', label: 'SSO' },
  { value: 'billing', label: 'Billing' },
];

function queryArgs() {
  return {
    from: dayjs(fromDate.value).startOf('day').toISOString(),
    to: dayjs(toDate.value).endOf('day').toISOString(),
    eventType: eventType.value || undefined,
    actorId: actorId.value || undefined,
    resourceType: resourceType.value || undefined,
  };
}

async function load(): Promise<void> {
  if (!workspaceId.value) return;
  loading.value = true;
  loadError.value = '';
  try {
    const result = await auditService.list(workspaceId.value, {
      ...queryArgs(),
      page: page.value,
      pageSize: pageSize.value,
    });
    rows.value = result.data;
    pagination.value = result.pagination;
    expanded.value = new Set();
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

function applyFilters(): void {
  page.value = 1;
  void load();
}

function toggle(id: string): void {
  const next = new Set(expanded.value);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  expanded.value = next;
}

function prettyMetadata(metadata: string | null): string {
  if (!metadata) return '';
  try {
    return JSON.stringify(JSON.parse(metadata), null, 2);
  } catch {
    return metadata;
  }
}

function formatTimestamp(value: string): string {
  return dayjs(value).format('D MMM YYYY, HH:mm:ss');
}

async function exportCsv(): Promise<void> {
  if (!workspaceId.value) return;
  exporting.value = true;
  try {
    const blob = await auditService.exportCsv(workspaceId.value, queryArgs());
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `audit-${fromDate.value}-to-${toDate.value}.csv`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
    toast.success('Audit log exported.');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    exporting.value = false;
  }
}

function goToPage(next: number): void {
  if (!pagination.value) return;
  if (next < 1 || next > pagination.value.totalPages) return;
  page.value = next;
  void load();
}

onMounted(load);
watch(() => ws.currentWorkspaceId.value, () => {
  page.value = 1;
  void load();
});
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6 p-6">
    <header class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-semibold text-slate-900">
          Audit Log
        </h1>
        <p class="text-sm text-slate-500">
          An immutable record of who did what, when, and from where across this workspace.
        </p>
      </div>
      <FmButton
        variant="secondary"
        :loading="exporting"
        @click="exportCsv"
      >
        <template #icon-left>
          <Download class="h-4 w-4" />
        </template>
        Export CSV
      </FmButton>
    </header>

    <!-- Filters -->
    <div class="grid grid-cols-1 gap-3 rounded-xl border border-slate-200 bg-white p-4 sm:grid-cols-2 lg:grid-cols-6">
      <label class="flex flex-col gap-1 text-xs font-medium text-slate-600">
        From
        <input
          v-model="fromDate"
          type="date"
          class="rounded-lg border border-slate-300 px-2.5 py-1.5 text-sm text-slate-800 focus:border-primary-500 focus:outline-none"
        >
      </label>
      <label class="flex flex-col gap-1 text-xs font-medium text-slate-600">
        To
        <input
          v-model="toDate"
          type="date"
          class="rounded-lg border border-slate-300 px-2.5 py-1.5 text-sm text-slate-800 focus:border-primary-500 focus:outline-none"
        >
      </label>
      <label class="flex flex-col gap-1 text-xs font-medium text-slate-600">
        Event type
        <select
          v-model="eventType"
          class="rounded-lg border border-slate-300 px-2.5 py-1.5 text-sm text-slate-800 focus:border-primary-500 focus:outline-none"
        >
          <option
            v-for="opt in eventTypeOptions"
            :key="opt.value"
            :value="opt.value"
          >
            {{ opt.label }}
          </option>
        </select>
      </label>
      <label class="flex flex-col gap-1 text-xs font-medium text-slate-600">
        Resource
        <select
          v-model="resourceType"
          class="rounded-lg border border-slate-300 px-2.5 py-1.5 text-sm text-slate-800 focus:border-primary-500 focus:outline-none"
        >
          <option
            v-for="opt in resourceTypeOptions"
            :key="opt.value"
            :value="opt.value"
          >
            {{ opt.label }}
          </option>
        </select>
      </label>
      <label class="flex flex-col gap-1 text-xs font-medium text-slate-600">
        Actor ID
        <input
          v-model="actorId"
          type="text"
          placeholder="User ID (optional)"
          class="rounded-lg border border-slate-300 px-2.5 py-1.5 text-sm text-slate-800 focus:border-primary-500 focus:outline-none"
        >
      </label>
      <div class="flex items-end">
        <FmButton
          class="w-full"
          @click="applyFilters"
        >
          Apply filters
        </FmButton>
      </div>
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load the audit log"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <FmEmptyState
      v-else-if="rows.length === 0"
      :icon="ScrollText"
      title="No activity yet"
      description="As people create workflows, trigger runs, decide gates, and invite members, every action will appear here. Try widening the date range or clearing filters to see more."
      cta-label="Reset to last 7 days"
      @cta="() => { fromDate = dayjs().subtract(7, 'day').format('YYYY-MM-DD'); toDate = dayjs().format('YYYY-MM-DD'); eventType = ''; resourceType = ''; actorId = ''; applyFilters(); }"
    />

    <div
      v-else
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <thead class="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
          <tr>
            <th class="w-8 px-3 py-3" />
            <th class="px-4 py-3">
              Timestamp
            </th>
            <th class="px-4 py-3">
              Actor
            </th>
            <th class="px-4 py-3">
              Action
            </th>
            <th class="px-4 py-3">
              Resource
            </th>
            <th class="px-4 py-3">
              IP address
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <template
            v-for="row in rows"
            :key="row.id"
          >
            <tr
              class="cursor-pointer hover:bg-slate-50"
              @click="toggle(row.id)"
            >
              <td class="px-3 py-3 text-slate-400">
                <ChevronDown
                  v-if="expanded.has(row.id)"
                  class="h-4 w-4"
                />
                <ChevronRight
                  v-else
                  class="h-4 w-4"
                />
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-500">
                {{ formatTimestamp(row.timestamp) }}
              </td>
              <td class="px-4 py-3 font-medium text-slate-800">
                {{ row.actor || '—' }}
                <span class="ml-1 text-xs font-normal text-slate-400">{{ row.actorType }}</span>
              </td>
              <td class="px-4 py-3 text-slate-600">
                {{ row.summary }}
              </td>
              <td class="px-4 py-3 text-slate-500">
                {{ row.resource || row.resourceType }}
              </td>
              <td class="whitespace-nowrap px-4 py-3 text-slate-500">
                {{ row.ipAddress || '—' }}
              </td>
            </tr>
            <tr v-if="expanded.has(row.id)">
              <td
                colspan="6"
                class="bg-slate-50 px-6 py-4"
              >
                <dl class="grid grid-cols-1 gap-x-8 gap-y-2 text-xs text-slate-600 sm:grid-cols-2">
                  <div>
                    <dt class="font-semibold text-slate-500">
                      Event type
                    </dt>
                    <dd>{{ row.eventType }}</dd>
                  </div>
                  <div>
                    <dt class="font-semibold text-slate-500">
                      User agent
                    </dt>
                    <dd class="break-all">
                      {{ row.userAgent || '—' }}
                    </dd>
                  </div>
                </dl>
                <div
                  v-if="row.metadata"
                  class="mt-3"
                >
                  <p class="text-xs font-semibold text-slate-500">
                    Details
                  </p>
                  <pre class="mt-1 overflow-x-auto rounded-lg bg-slate-900 p-3 text-xs text-slate-100">{{ prettyMetadata(row.metadata) }}</pre>
                </div>
                <p
                  v-else
                  class="mt-3 text-xs text-slate-400"
                >
                  No additional details recorded for this event.
                </p>
              </td>
            </tr>
          </template>
        </tbody>
      </table>

      <!-- Pagination -->
      <div
        v-if="pagination"
        class="flex items-center justify-between border-t border-slate-100 px-4 py-3 text-sm text-slate-500"
      >
        <span>
          {{ pagination.total }} event{{ pagination.total === 1 ? '' : 's' }} ·
          page {{ pagination.page }} of {{ Math.max(pagination.totalPages, 1) }}
        </span>
        <div class="flex gap-2">
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-3 py-1.5 font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-40"
            :disabled="pagination.page <= 1"
            @click="goToPage(pagination.page - 1)"
          >
            Previous
          </button>
          <button
            type="button"
            class="rounded-lg border border-slate-200 px-3 py-1.5 font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-40"
            :disabled="pagination.page >= pagination.totalPages"
            @click="goToPage(pagination.page + 1)"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
