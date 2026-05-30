<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { Check, X } from 'lucide-vue-next';
import { storeToRefs } from 'pinia';
import { useWorkspace } from '@/composables/useWorkspace';
import { useWorkflowStore } from '@/stores/workflow.store';
import { preferencesService } from '@/services/preferences.service';

const props = defineProps<{
  runsThisMonth?: number;
}>();

const ws = useWorkspace();
const workflowStore = useWorkflowStore();
const { workflows } = storeToRefs(workflowStore);
const router = useRouter();

const workspaceId = computed(() => ws.currentWorkspaceId.value ?? 'default');
const prefKey = computed(() => `checklist_${workspaceId.value}`);
const dismissed = ref(false);

interface ChecklistState { dismissed?: boolean; completedItems?: string[] }

async function loadState(): Promise<void> {
  try {
    const state = await preferencesService.get<ChecklistState>(prefKey.value);
    dismissed.value = state?.dismissed === true;
  } catch {
    dismissed.value = false;
  }
}

onMounted(loadState);
watch(workspaceId, loadState);

// Phase 1: no connector API yet
const installedConnectorsCount = ref(0);

const items = computed(() => [
  {
    id: 'first-workflow',
    title: 'Create your first workflow',
    description: 'Describe what you want to automate in plain English.',
    to: '/workflows/new',
    done: workflows.value.length > 0,
  },
  {
    id: 'connect-system',
    title: 'Connect a system',
    description: 'Link Slack, your database, or any API via the Library.',
    to: '/library',
    done: installedConnectorsCount.value > 0,
  },
  {
    id: 'first-run',
    title: 'Trigger your first run',
    description: 'Watch a workflow execute end to end.',
    to: '/instances',
    done: (props.runsThisMonth ?? 0) > 0,
  },
  {
    id: 'invite-member',
    title: 'Invite a team member',
    description: 'Add a teammate to your workspace.',
    to: '/settings/members',
    done: ws.members.value.length > 1,
  },
]);

const completedCount = computed(() => items.value.filter((i) => i.done).length);
const showChecklist = computed(() => !dismissed.value && completedCount.value < 4);

function dismiss(): void {
  dismissed.value = true;
  // Persist server-side so the dismissal follows the user across devices.
  void preferencesService.set<ChecklistState>(prefKey.value, {
    dismissed: true,
    completedItems: items.value.filter((i) => i.done).map((i) => i.id),
  });
}
</script>

<template>
  <section
    v-if="showChecklist"
    class="rounded-xl border border-slate-200 bg-white p-5"
  >
    <div class="flex items-start justify-between">
      <div>
        <h2 class="text-base font-semibold text-slate-900">
          Get started with Flowamaz
        </h2>
        <p class="mt-0.5 text-sm text-slate-500">
          {{ completedCount }} of 4 complete
        </p>
      </div>
      <button
        type="button"
        class="flex items-center gap-1 text-xs text-slate-400 hover:text-slate-600"
        @click="dismiss"
      >
        <X class="h-3.5 w-3.5" />
        I'll explore on my own
      </button>
    </div>

    <ul class="mt-4 divide-y divide-slate-100">
      <!-- Completed item -->
      <li
        v-for="item in items.filter((i) => i.done)"
        :key="item.id"
        class="flex items-center gap-3 py-4 opacity-60"
      >
        <div class="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-teal-500">
          <Check class="h-3.5 w-3.5 text-white" />
        </div>
        <div class="flex-1">
          <p class="text-sm font-medium text-gray-500 line-through">
            {{ item.title }}
          </p>
          <p class="text-xs text-gray-400">
            {{ item.description }}
          </p>
        </div>
      </li>

      <!-- Incomplete item -->
      <li
        v-for="item in items.filter((i) => !i.done)"
        :key="item.id"
        class="-mx-2 flex cursor-pointer items-center gap-3 rounded-lg px-2 py-4 transition-colors hover:bg-gray-50"
        @click="router.push(item.to)"
      >
        <div class="h-6 w-6 shrink-0 rounded-full border-2 border-gray-300" />
        <div class="flex-1">
          <p class="text-sm font-medium text-gray-900">
            {{ item.title }}
          </p>
          <p class="text-xs text-gray-500">
            {{ item.description }}
          </p>
        </div>
        <span class="shrink-0 text-sm font-medium text-teal-600">Start →</span>
      </li>
    </ul>
  </section>
</template>
