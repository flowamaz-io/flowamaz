<script setup lang="ts">
import { computed, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { Check, Circle, X } from 'lucide-vue-next';
import { useAuth } from '@/composables/useAuth';
import { useWorkspace } from '@/composables/useWorkspace';
import { dismissChecklist, isChecklistDismissed } from '@/composables/useOnboarding';

const { user } = useAuth();
const ws = useWorkspace();

const orgId = computed(() => user.value?.orgId ?? 'anon');
const dismissed = ref(isChecklistDismissed(orgId.value));

// Phase 1: only "Invite a team member" is actionable; the rest preview Phase 2/3/4.
// A member is considered invited once the workspace has more than the lone owner.
const teamInvited = computed(() => ws.members.value.length > 1);

const items = computed(() => [
  {
    title: 'Create your first workflow',
    description: 'Describe what you want to automate in plain English.',
    to: '/workflows/new',
    enabled: false,
    hint: 'Coming next',
    done: false,
  },
  {
    title: 'Connect a system',
    description: 'Link Slack, your database, or any API via the Library.',
    to: '/library',
    enabled: false,
    hint: 'Phase 4',
    done: false,
  },
  {
    title: 'Trigger your first run',
    description: 'Watch a workflow execute end to end.',
    to: '',
    enabled: false,
    hint: 'Phase 3',
    done: false,
  },
  {
    title: 'Invite a team member',
    description: 'Add a teammate to your workspace.',
    to: '/settings/members',
    enabled: true,
    hint: '',
    done: teamInvited.value,
  },
]);

const completedCount = computed(() => items.value.filter((i) => i.done).length);

function dismiss(): void {
  dismissChecklist(orgId.value);
  dismissed.value = true;
}
</script>

<template>
  <section
    v-if="!dismissed"
    class="rounded-xl border border-slate-200 bg-white p-5"
  >
    <div class="flex items-start justify-between">
      <div>
        <h2 class="text-base font-semibold text-slate-900">
          Get started with Flowamaz
        </h2>
        <p class="mt-0.5 text-sm text-slate-500">
          {{ completedCount }} of {{ items.length }} complete
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

    <ul class="mt-4 space-y-2">
      <li
        v-for="item in items"
        :key="item.title"
        class="flex items-center gap-3 rounded-lg border border-slate-100 px-3 py-3"
      >
        <span
          :class="[
            'flex h-5 w-5 shrink-0 items-center justify-center rounded-full',
            item.done ? 'bg-primary-600 text-white' : 'border border-slate-300 text-transparent',
          ]"
        >
          <Check
            v-if="item.done"
            class="h-3 w-3"
          />
          <Circle
            v-else
            class="h-3 w-3 text-transparent"
          />
        </span>
        <div class="min-w-0 flex-1">
          <p :class="['text-sm font-medium', item.enabled ? 'text-slate-800' : 'text-slate-400']">
            {{ item.title }}
          </p>
          <p class="truncate text-xs text-slate-400">
            {{ item.description }}
          </p>
        </div>
        <RouterLink
          v-if="item.enabled && item.to"
          :to="item.to"
          class="shrink-0 text-sm font-medium text-primary-600 hover:text-primary-700"
        >
          Start →
        </RouterLink>
        <span
          v-else
          class="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-400"
        >
          {{ item.hint }}
        </span>
      </li>
    </ul>
  </section>
</template>
