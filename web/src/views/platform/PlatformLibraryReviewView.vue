<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { CheckCircle2, ClipboardCheck, ExternalLink, XCircle } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useAuth } from '@/composables/useAuth';
import {
  libraryPublisherService, submissionBadge, type LibraryReviewQueue, type LibraryReviewTemplate,
} from '@/services/libraryPublisher.service';
import { toUserFacingError } from '@/utils/error.util';
import { formatDate } from '@/utils/date.util';

// Flowamaz staff: templates published from the app wait here for a decision (FUNCTIONAL.md §23.9).
// Connector submissions are decided on GitHub; they are listed for context only.
const router = useRouter();
const auth = useAuth();
const isStaff = computed(() => auth.user.value?.isPlatformStaff === true);

const queue = ref<LibraryReviewQueue | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);
const open = ref<string | null>(null);
const notes = ref<Record<string, string>>({});
const busy = ref<string | null>(null);
const problems = ref<Record<string, string>>({});
const decided = ref<{ name: string; approved: boolean }[]>([]);

async function load(): Promise<void> {
  if (!isStaff.value) return;
  loading.value = true;
  error.value = null;
  try {
    queue.value = await libraryPublisherService.reviewQueue();
  } catch (e) {
    error.value = toUserFacingError(e).message || 'The review queue could not be loaded. Check your connection, then select "Try again".';
  } finally {
    loading.value = false;
  }
}

async function decide(t: LibraryReviewTemplate, approve: boolean): Promise<void> {
  const note = (notes.value[t.id] ?? '').trim();
  if (!approve && note.length < 10) {
    problems.value = { ...problems.value, [t.id]: 'Say why it is rejected (at least 10 characters). The publisher sees this note and uses it to fix the template.' };
    return;
  }
  busy.value = t.id;
  delete problems.value[t.id];
  try {
    if (approve) await libraryPublisherService.approve(t.id, note || undefined);
    else await libraryPublisherService.reject(t.id, note);
    if (queue.value) queue.value = { ...queue.value, templates: queue.value.templates.filter((x) => x.id !== t.id) };
    decided.value = [{ name: t.name, approved: approve }, ...decided.value];
  } catch (e) {
    problems.value = { ...problems.value, [t.id]: toUserFacingError(e).message || 'The decision was not saved. Reload the queue and try again.' };
  } finally {
    busy.value = null;
  }
}

onMounted(load);
</script>

<template>
  <div
    class="mx-auto max-w-5xl space-y-6 p-6"
    data-testid="library-reviews"
  >
    <header>
      <h1 class="text-2xl font-semibold text-slate-900">
        Library reviews
      </h1>
      <p class="text-sm text-slate-500">
        Templates published from the app appear in the Library only after a Flowamaz reviewer approves them.
        Publishers see your note and are notified either way.
      </p>
    </header>

    <FmEmptyState
      v-if="!isStaff"
      :icon="ClipboardCheck"
      title="This page is for Flowamaz staff"
      description="Library reviews are done by the Flowamaz team. To see where your own submissions stand, open Settings → Publisher."
      cta-label="Open Settings → Publisher"
      @cta="router.push('/settings/publisher')"
    />
    <div
      v-else-if="loading"
      class="flex justify-center py-16"
    >
      <FmSpinner size="lg" />
    </div>
    <FmErrorState
      v-else-if="error"
      title="Couldn't load the review queue"
      :description="error"
      action-label="Try again"
      @action="load"
    />
    <template v-else-if="queue">
      <ul
        v-if="decided.length > 0"
        class="space-y-1 text-sm"
        role="status"
      >
        <li
          v-for="d in decided"
          :key="d.name"
          class="flex items-center gap-1"
          :class="d.approved ? 'text-emerald-700' : 'text-slate-600'"
        >
          <CheckCircle2
            v-if="d.approved"
            class="h-4 w-4"
          />
          <XCircle
            v-else
            class="h-4 w-4"
          />
          {{ d.name }} {{ d.approved ? 'approved — it is now in the Library.' : 'rejected — the publisher has your note.' }}
        </li>
      </ul>

      <section class="space-y-3">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">
          Templates waiting ({{ queue.templates.length }})
        </h2>
        <FmEmptyState
          v-if="queue.templates.length === 0"
          :icon="ClipboardCheck"
          title="Nothing to review"
          description="New templates published from the app show up here, oldest first. Check back later, or reload."
          cta-label="Reload"
          @cta="load"
        />
        <article
          v-for="t in queue.templates"
          :key="t.id"
          class="rounded-xl border border-slate-200 bg-white p-5"
          :data-testid="`review-template-${t.slug}`"
        >
          <div class="flex flex-wrap items-start justify-between gap-2">
            <div>
              <h3 class="font-semibold text-slate-900">
                {{ t.name }}
                <span class="ml-1 text-xs font-normal text-slate-500">v{{ t.version }} · {{ t.category || 'No category' }}</span>
              </h3>
              <p class="text-xs text-slate-500">
                From
                <router-link
                  :to="`/library/publishers/${t.publisherId}`"
                  class="text-indigo-600 hover:text-indigo-800"
                >
                  {{ t.orgName }} ({{ t.publisherId }})
                </router-link>
                · submitted {{ formatDate(t.submittedAt) }}
              </p>
            </div>
            <button
              type="button"
              class="text-xs font-medium text-indigo-600 hover:text-indigo-800"
              @click="open = open === t.id ? null : t.id"
            >
              {{ open === t.id ? 'Hide YAML' : 'Show YAML' }}
            </button>
          </div>
          <p
            v-if="t.description"
            class="mt-2 text-sm text-slate-600"
          >
            {{ t.description }}
          </p>
          <pre
            v-if="open === t.id"
            class="mt-3 max-h-96 overflow-auto rounded-lg bg-slate-900 p-3 text-xs text-slate-100"
          >{{ t.yamlContent }}</pre>
          <label class="mt-3 block">
            <span class="text-xs font-medium text-slate-600">Note to the publisher (required to reject)</span>
            <textarea
              v-model="notes[t.id]"
              rows="2"
              maxlength="1000"
              class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-indigo-400 focus:outline-none focus:ring-2 focus:ring-indigo-100"
              :placeholder="'e.g. The approval step has no assignee; add one before the end.'"
              @input="delete problems[t.id]"
            />
          </label>
          <p
            v-if="problems[t.id]"
            class="mt-1 text-sm text-red-600"
            role="alert"
          >
            {{ problems[t.id] }}
          </p>
          <div class="mt-3 flex gap-2">
            <FmButton
              :loading="busy === t.id"
              :data-testid="`review-approve-${t.slug}`"
              @click="decide(t, true)"
            >
              Approve
            </FmButton>
            <FmButton
              variant="secondary"
              :disabled="busy === t.id"
              :data-testid="`review-reject-${t.slug}`"
              @click="decide(t, false)"
            >
              Reject
            </FmButton>
          </div>
        </article>
      </section>

      <section class="space-y-2">
        <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">
          Connector pull requests ({{ queue.connectorPullRequests.length }})
        </h2>
        <p class="text-sm text-slate-500">
          Connectors are reviewed on GitHub (§23.6): they auto-merge when checks pass, or wait for a review.
          These are the ones not live yet.
        </p>
        <p
          v-if="queue.connectorPullRequests.length === 0"
          class="text-sm text-slate-400"
        >
          No open connector submissions.
        </p>
        <ul
          v-else
          class="divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white"
        >
          <li
            v-for="s in queue.connectorPullRequests"
            :key="s.id"
            class="flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-sm"
          >
            <span>
              <span class="font-medium text-slate-900">{{ s.name }}</span>
              <span class="ml-1 font-mono text-xs text-slate-500">{{ s.libraryId }}</span>
              <span class="ml-1 text-xs text-slate-400">· {{ formatDate(s.submittedAt) }}</span>
            </span>
            <span class="flex items-center gap-3">
              <FmBadge :variant="submissionBadge(s.status).variant">{{ submissionBadge(s.status).label }}</FmBadge>
              <a
                v-if="s.pullRequestUrl"
                :href="s.pullRequestUrl"
                target="_blank"
                rel="noopener noreferrer"
                class="inline-flex items-center gap-1 text-indigo-600 hover:text-indigo-800"
              >Open on GitHub <ExternalLink class="h-3 w-3" /></a>
            </span>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>
