<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { BookOpen, ExternalLink, Pencil, Send } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import {
  KIND_LABELS, itemLink, libraryPublisherService, submissionBadge, type PublisherPortal,
} from '@/services/libraryPublisher.service';
import { toUserFacingError } from '@/utils/error.util';
import { formatDate } from '@/utils/date.util';

// The publisher portal's Library side (FUNCTIONAL.md §23.9): the public profile, live items with their
// numbers, and every submission with where it stands. Org owner only, like the rest of this page.
const router = useRouter();

const portal = ref<PublisherPortal | null>(null);
const loading = ref(true);
const error = ref<string | null>(null);

const editing = ref(false);
const saving = ref(false);
const saveError = ref<string | null>(null);
const saved = ref(false);
const draft = ref({ displayName: '', bio: '', websiteUrl: '', githubLogin: '' });

const profile = computed(() => portal.value?.profile ?? null);
const items = computed(() => profile.value?.items ?? []);
const submissions = computed(() => portal.value?.submissions ?? []);
const bioLeft = computed(() => 600 - draft.value.bio.length);

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  try {
    portal.value = await libraryPublisherService.portal();
  } catch (e) {
    error.value = toUserFacingError(e).message || 'Your Library publisher details could not be loaded. Check your connection, then select "Try again".';
  } finally {
    loading.value = false;
  }
}

function startEdit(): void {
  const p = profile.value;
  draft.value = {
    // A new profile starts empty (the org name is the placeholder), so typing doesn't append to it.
    displayName: portal.value?.hasProfile ? p?.displayName ?? '' : '',
    bio: p?.bio ?? '',
    websiteUrl: p?.websiteUrl ?? '',
    githubLogin: p?.githubLogin ?? '',
  };
  saveError.value = null;
  saved.value = false;
  editing.value = true;
}

async function save(): Promise<void> {
  if (!portal.value) return;
  saving.value = true;
  saveError.value = null;
  try {
    const updated = await libraryPublisherService.updateProfile({
      displayName: draft.value.displayName.trim(),
      bio: draft.value.bio.trim() || undefined,
      websiteUrl: draft.value.websiteUrl.trim() || undefined,
      githubLogin: draft.value.githubLogin.trim() || undefined,
    });
    portal.value = { ...portal.value, hasProfile: true, profile: updated };
    editing.value = false;
    saved.value = true;
  } catch (e) {
    saveError.value = toUserFacingError(e).message || 'The profile was not saved. Check your connection and save again.';
  } finally {
    saving.value = false;
  }
}

const inputClass = 'w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-900 focus:border-indigo-400 focus:outline-none focus:ring-2 focus:ring-indigo-100';

onMounted(load);
</script>

<template>
  <section
    class="space-y-5 rounded-xl border border-slate-200 bg-white p-6"
    data-testid="library-publisher"
  >
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 class="flex items-center gap-2 text-base font-semibold text-slate-900">
          <BookOpen class="h-4 w-4 text-indigo-600" /> Library publisher
        </h2>
        <p class="text-sm text-slate-500">
          Your free Library profile, what you publish and how it's doing, and where each submission stands.
        </p>
      </div>
      <router-link
        v-if="portal"
        :to="`/library/publishers/${portal.publisherId}`"
        class="inline-flex items-center gap-1 text-sm font-medium text-indigo-600 hover:text-indigo-800"
        data-testid="library-publisher-public-link"
      >
        View public profile <ExternalLink class="h-3.5 w-3.5" />
      </router-link>
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-8"
    >
      <FmSpinner />
    </div>
    <FmErrorState
      v-else-if="error"
      title="Couldn't load your Library publisher details"
      :description="error"
      action-label="Try again"
      @action="load"
    />
    <template v-else-if="portal && profile">
      <!-- Profile -->
      <div class="rounded-lg border border-slate-100 bg-slate-50 p-4">
        <FmAlert
          v-if="saved"
          type="success"
          class="mb-3"
        >
          Profile saved. It's what everyone sees on your publisher page.
        </FmAlert>
        <form
          v-if="editing"
          class="space-y-3"
          data-testid="library-publisher-form"
          @submit.prevent="save"
        >
          <label class="block">
            <span class="text-sm font-medium text-slate-700">Display name</span>
            <input
              v-model="draft.displayName"
              :class="inputClass"
              maxlength="100"
              :placeholder="profile.displayName"
              required
            >
          </label>
          <label class="block">
            <span class="text-sm font-medium text-slate-700">Description</span>
            <textarea
              v-model="draft.bio"
              :class="inputClass"
              rows="3"
              maxlength="600"
              placeholder="What you build and who it's for."
            />
            <span class="text-xs text-slate-400">{{ bioLeft }} characters left</span>
          </label>
          <div class="grid gap-3 sm:grid-cols-2">
            <label class="block">
              <span class="text-sm font-medium text-slate-700">Website</span>
              <input
                v-model="draft.websiteUrl"
                :class="inputClass"
                type="url"
                placeholder="https://acme.example"
              >
            </label>
            <label class="block">
              <span class="text-sm font-medium text-slate-700">GitHub account</span>
              <input
                v-model="draft.githubLogin"
                :class="inputClass"
                placeholder="acme-labs"
              >
            </label>
          </div>
          <FmAlert
            v-if="saveError"
            type="error"
          >
            {{ saveError }}
          </FmAlert>
          <div class="flex gap-2">
            <FmButton
              :loading="saving"
              data-testid="library-publisher-save"
              @click="save"
            >
              Save profile
            </FmButton>
            <FmButton
              variant="secondary"
              :disabled="saving"
              @click="editing = false"
            >
              Cancel
            </FmButton>
          </div>
        </form>
        <div
          v-else
          class="flex flex-wrap items-start justify-between gap-3"
        >
          <div class="min-w-0">
            <p class="font-medium text-slate-900">
              {{ profile.displayName }}
              <span class="ml-1 font-mono text-xs text-slate-500">{{ portal.publisherId }}</span>
              <FmBadge
                v-if="profile.isVerified"
                variant="primary"
                class="ml-1"
              >
                Verified
              </FmBadge>
            </p>
            <p
              class="mt-1 text-sm"
              :class="profile.bio ? 'text-slate-600' : 'text-slate-400'"
            >
              {{ profile.bio || (portal.hasProfile ? 'No description.' : 'No profile yet — people see only your organisation name. Add a description and links so they know who maintains your connectors.') }}
            </p>
          </div>
          <FmButton
            variant="secondary"
            data-testid="library-publisher-edit"
            @click="startEdit"
          >
            <Pencil class="h-3.5 w-3.5" /> {{ portal.hasProfile ? 'Edit profile' : 'Create profile' }}
          </FmButton>
        </div>
      </div>

      <!-- Items -->
      <div class="space-y-2">
        <h3 class="text-sm font-medium text-slate-700">
          In the Library ({{ items.length }})
        </h3>
        <p
          v-if="items.length === 0"
          class="rounded-lg border border-dashed border-slate-300 p-4 text-sm text-slate-500"
          data-testid="library-publisher-no-items"
        >
          Nothing live yet. Publish a workflow, then use "Publish as template" on its page, or publish a connector with
          <code>fmz connector publish</code>. Items appear here, with their installs and ratings, once they're live.
        </p>
        <div
          v-else
          class="overflow-x-auto"
        >
          <table class="w-full text-left text-sm">
            <thead class="text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th class="py-2 pr-3 font-medium">
                  Item
                </th>
                <th class="py-2 pr-3 font-medium">
                  Version
                </th>
                <th class="py-2 pr-3 font-medium">
                  Installs
                </th>
                <th class="py-2 pr-3 font-medium">
                  Rating
                </th>
                <th class="py-2 font-medium">
                  Status
                </th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100">
              <tr
                v-for="item in items"
                :key="item.kind + item.id"
              >
                <td class="py-2 pr-3">
                  <router-link
                    :to="itemLink(item)"
                    class="font-medium text-slate-900 hover:text-indigo-700"
                  >
                    {{ item.name }}
                  </router-link>
                  <span class="ml-1 text-xs text-slate-400">{{ KIND_LABELS[item.kind].replace(/s$/, '') }}</span>
                </td>
                <td class="py-2 pr-3 text-slate-600">
                  {{ item.version }}
                </td>
                <td class="py-2 pr-3 text-slate-600">
                  {{ item.kind === 'connector' || item.kind === 'template' ? item.installs.toLocaleString() : '—' }}
                </td>
                <td class="py-2 pr-3 text-slate-600">
                  {{ item.ratingCount > 0 ? `${item.averageRating.toFixed(1)} ★ (${item.ratingCount})` : '—' }}
                </td>
                <td class="py-2 text-xs">
                  <span
                    v-if="item.deprecatedAt"
                    class="text-amber-700"
                  >Deprecated {{ formatDate(item.deprecatedAt) }}</span>
                  <span
                    v-else
                    class="text-emerald-700"
                  >Live</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Submissions -->
      <div class="space-y-2">
        <h3 class="text-sm font-medium text-slate-700">
          Submissions ({{ submissions.length }})
        </h3>
        <p
          v-if="submissions.length === 0"
          class="rounded-lg border border-dashed border-slate-300 p-4 text-sm text-slate-500"
          data-testid="library-publisher-no-submissions"
        >
          You haven't submitted anything to the Library yet. Connectors go in as a pull request
          (<code>fmz connector publish</code>); templates you publish from the app are checked by a Flowamaz reviewer.
          <button
            type="button"
            class="ml-1 inline-flex items-center gap-1 font-medium text-indigo-600 hover:text-indigo-800"
            @click="router.push('/library/submit')"
          >
            <Send class="h-3.5 w-3.5" /> Submit a connector
          </button>
        </p>
        <ul
          v-else
          class="divide-y divide-slate-100"
        >
          <li
            v-for="s in submissions"
            :key="s.kind + s.id"
            class="py-3"
            :data-testid="`library-submission-${s.kind}`"
          >
            <div class="flex flex-wrap items-center justify-between gap-2">
              <p class="text-sm font-medium text-slate-900">
                {{ s.name }}
                <span class="ml-1 text-xs font-normal text-slate-400">{{ s.kind }} · submitted {{ formatDate(s.submittedAt) }}</span>
              </p>
              <FmBadge :variant="submissionBadge(s.status).variant">
                {{ submissionBadge(s.status).label }}
              </FmBadge>
            </div>
            <p class="mt-1 text-sm text-slate-600">
              {{ s.statusText }}
            </p>
            <p
              v-if="s.reviewerNotes"
              class="mt-1 rounded bg-slate-50 px-2 py-1 text-sm text-slate-700"
            >
              <span class="font-medium">Reviewer's note:</span> {{ s.reviewerNotes }}
            </p>
            <p class="mt-1 flex flex-wrap gap-3 text-xs text-slate-400">
              <a
                v-if="s.pullRequestUrl"
                :href="s.pullRequestUrl"
                target="_blank"
                rel="noopener noreferrer"
                class="inline-flex items-center gap-1 text-indigo-600 hover:text-indigo-800"
              >Pull request <ExternalLink class="h-3 w-3" /></a>
              <span v-if="s.checkedAt">GitHub checked {{ formatDate(s.checkedAt) }}</span>
              <span v-if="s.decidedAt">Decided {{ formatDate(s.decidedAt) }}</span>
            </p>
          </li>
        </ul>
      </div>
    </template>
  </section>
</template>
