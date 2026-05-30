<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { Plus, LayoutGrid, Workflow, Users } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import CreateWorkspaceModal from '@/components/workspace/CreateWorkspaceModal.vue';
import { workspaceService } from '@/services/workspace.service';
import { useWorkspace } from '@/composables/useWorkspace';
import { toUserFacingError } from '@/utils/error.util';
import { avatarColor, initial } from '@/utils/string.util';
import { fromNow } from '@/utils/date.util';
import type { WorkspaceOverviewResponse } from '@/types';

const router = useRouter();
const ws = useWorkspace();

const workspaces = ref<WorkspaceOverviewResponse[]>([]);
const loading = ref(true);
const loadError = ref('');
const createOpen = ref(false);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    workspaces.value = await workspaceService.overview();
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);

function open(id: string): void {
  ws.switchWorkspace(id);
  router.push({ name: 'dashboard' });
}

function onCreated(): void {
  void load();
}
</script>

<template>
  <div class="mx-auto max-w-5xl space-y-6 p-6">
    <header class="flex items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold text-slate-900">
          Workspaces
        </h1>
        <p class="text-sm text-slate-500">
          Every workspace you belong to. Switch into one to manage its workflows and runs.
        </p>
      </div>
      <FmButton @click="createOpen = true">
        <template #icon-left>
          <Plus class="h-4 w-4" />
        </template>
        New workspace
      </FmButton>
    </header>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load your workspaces"
      :description="loadError"
      action-label="Try again"
      help-article="workspaces/what-is-a-workspace"
      @action="load"
    />

    <FmEmptyState
      v-else-if="workspaces.length === 0"
      :icon="LayoutGrid"
      title="No workspaces yet"
      description="Workspaces keep workflows, runs, and members isolated. Create your first one to get started."
      cta-label="Create workspace"
      @cta="createOpen = true"
    />

    <div
      v-else
      class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
    >
      <article
        v-for="w in workspaces"
        :key="w.id"
        class="flex flex-col rounded-xl border border-slate-200 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
      >
        <div class="flex items-start gap-3">
          <span
            :class="[
              'flex h-10 w-10 shrink-0 items-center justify-center rounded-lg text-base font-semibold text-white',
              avatarColor(w.name),
            ]"
          >
            {{ initial(w.name) }}
          </span>
          <div class="min-w-0 flex-1">
            <h2 class="truncate text-base font-semibold text-slate-900">
              {{ w.name }}
            </h2>
            <p class="truncate text-xs text-slate-500">
              flowamaz.io/{{ w.slug }}
            </p>
          </div>
        </div>

        <div class="mt-3 flex flex-wrap items-center gap-2">
          <FmBadge variant="primary">
            {{ w.userRole }}
          </FmBadge>
          <FmBadge
            v-if="w.status === 'Archived'"
            variant="amber"
          >
            Archived
          </FmBadge>
        </div>

        <dl class="mt-4 grid grid-cols-2 gap-3 text-sm">
          <div class="flex items-center gap-1.5 text-slate-600">
            <Workflow class="h-4 w-4 text-slate-400" />
            <span>{{ w.workflowCount }} workflows</span>
          </div>
          <div class="flex items-center gap-1.5 text-slate-600">
            <Users class="h-4 w-4 text-slate-400" />
            <span>{{ w.memberCount }} members</span>
          </div>
        </dl>
        <p class="mt-2 text-xs text-slate-400">
          Active {{ fromNow(w.lastActiveAt) }}
        </p>

        <div class="mt-4 flex justify-end">
          <FmButton
            variant="secondary"
            size="sm"
            @click="open(w.id)"
          >
            Open
          </FmButton>
        </div>
      </article>
    </div>

    <CreateWorkspaceModal
      :open="createOpen"
      @close="createOpen = false"
      @created="onCreated"
    />
  </div>
</template>
