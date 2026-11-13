<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { BadgeCheck, ExternalLink, Github, Globe, Package } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import {
  KIND_LABELS, itemLink, libraryPublisherService, type PublisherItemKind, type PublisherProfile,
} from '@/services/libraryPublisher.service';
import { toUserFacingError } from '@/utils/error.util';
import { formatDate } from '@/utils/date.util';

// A Library publisher's public profile (FUNCTIONAL.md §23.9): who they are and everything they publish.
const route = useRoute();
const router = useRouter();

const profile = ref<PublisherProfile | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);
const notFound = ref(false);

const publisherId = computed(() => String(route.params.publisherId ?? ''));
const kinds: PublisherItemKind[] = ['connector', 'template', 'form', 'pack'];
const initial = computed(() => (profile.value?.displayName.trim()[0] ?? '?').toUpperCase());

function installsLabel(n: number): string {
  return `${n.toLocaleString()} ${n === 1 ? 'install' : 'installs'}`;
}

function itemsOf(kind: PublisherItemKind): PublisherProfile['items'] {
  return profile.value?.items.filter((i) => i.kind === kind) ?? [];
}

async function load(): Promise<void> {
  if (!publisherId.value) return;
  loading.value = true;
  error.value = null;
  notFound.value = false;
  try {
    profile.value = await libraryPublisherService.profile(publisherId.value);
  } catch (e) {
    const err = toUserFacingError(e);
    if (err.code === 'LIBRARY_PUBLISHER_NOT_FOUND' || err.code === 'NOT_FOUND') notFound.value = true;
    else error.value = err.message || 'The profile could not be loaded. Check your connection, then select "Try again".';
  } finally {
    loading.value = false;
  }
}

watch(publisherId, load, { immediate: true });
</script>

<template>
  <div
    class="mx-auto max-w-5xl space-y-6 p-6"
    data-testid="publisher-profile"
  >
    <div
      v-if="loading"
      class="flex justify-center py-16"
    >
      <FmSpinner size="lg" />
    </div>
    <FmEmptyState
      v-else-if="notFound"
      :icon="Package"
      title="No such publisher"
      :description="`There is no Library publisher called “${publisherId}”. Publisher ids are the first part of a pin, like acme in acme/invoice-sync@1. Open an item in the Library and follow its publisher link.`"
      cta-label="Browse the Library"
      @cta="router.push('/library')"
    />
    <FmErrorState
      v-else-if="error"
      title="Couldn't load this publisher"
      :description="error"
      action-label="Try again"
      @action="load"
    />
    <template v-else-if="profile">
      <header class="flex flex-wrap items-start gap-4 rounded-xl border border-slate-200 bg-white p-6">
        <div class="flex h-14 w-14 shrink-0 items-center justify-center rounded-xl bg-indigo-100 text-2xl font-semibold text-indigo-700">
          {{ initial }}
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex flex-wrap items-center gap-2">
            <h1 class="text-2xl font-semibold text-slate-900">
              {{ profile.displayName }}
            </h1>
            <FmBadge
              v-if="profile.isOfficial"
              variant="primary"
            >
              Official
            </FmBadge>
            <span
              v-else-if="profile.isVerified"
              class="inline-flex items-center gap-1 text-xs font-medium text-indigo-700"
              data-testid="publisher-verified"
            ><BadgeCheck class="h-4 w-4" /> Verified</span>
          </div>
          <p class="font-mono text-xs text-slate-500">
            {{ profile.publisherId }}
          </p>
          <p
            v-if="profile.bio"
            class="mt-2 whitespace-pre-line text-sm text-slate-700"
          >
            {{ profile.bio }}
          </p>
          <p
            v-else
            class="mt-2 text-sm text-slate-500"
          >
            This publisher hasn't written a description yet.
          </p>
          <div class="mt-3 flex flex-wrap gap-4 text-sm">
            <a
              v-if="profile.websiteUrl"
              :href="profile.websiteUrl"
              target="_blank"
              rel="noopener noreferrer nofollow"
              class="inline-flex items-center gap-1 text-indigo-600 hover:text-indigo-800"
            ><Globe class="h-4 w-4" /> {{ profile.websiteUrl.replace(/^https:\/\//, '') }} <ExternalLink class="h-3 w-3" /></a>
            <a
              v-if="profile.githubLogin"
              :href="`https://github.com/${profile.githubLogin}`"
              target="_blank"
              rel="noopener noreferrer nofollow"
              class="inline-flex items-center gap-1 text-indigo-600 hover:text-indigo-800"
            ><Github class="h-4 w-4" /> {{ profile.githubLogin }} <ExternalLink class="h-3 w-3" /></a>
          </div>
        </div>
        <dl class="grid grid-cols-3 gap-4 text-center">
          <div>
            <dt class="text-xs text-slate-500">
              Items
            </dt>
            <dd class="text-lg font-semibold text-slate-900">
              {{ profile.totals.items }}
            </dd>
          </div>
          <div>
            <dt class="text-xs text-slate-500">
              Installs
            </dt>
            <dd class="text-lg font-semibold text-slate-900">
              {{ profile.totals.installs.toLocaleString() }}
            </dd>
          </div>
          <div>
            <dt class="text-xs text-slate-500">
              Rating
            </dt>
            <dd class="text-lg font-semibold text-slate-900">
              {{ profile.totals.ratingCount > 0 ? `${profile.totals.averageRating.toFixed(1)} ★` : '—' }}
            </dd>
          </div>
        </dl>
      </header>

      <FmEmptyState
        v-if="profile.items.length === 0"
        :icon="Package"
        title="Nothing published yet"
        description="When this publisher's first connector or template is live in the Library, it appears here."
        cta-label="Browse the Library"
        @cta="router.push('/library')"
      />

      <template
        v-for="kind in kinds"
        :key="kind"
      >
        <section
          v-if="itemsOf(kind).length > 0"
          class="space-y-2"
          :data-testid="`publisher-items-${kind}`"
        >
          <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-500">
            {{ KIND_LABELS[kind] }} ({{ itemsOf(kind).length }})
          </h2>
          <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <router-link
              v-for="item in itemsOf(kind)"
              :key="item.id"
              :to="itemLink(item)"
              class="rounded-xl border border-slate-200 bg-white p-4 transition-colors hover:border-indigo-300"
            >
              <p class="font-medium text-slate-900">
                {{ item.name }}
              </p>
              <p class="text-xs text-slate-500">
                {{ item.category }} · v{{ item.version }}
              </p>
              <p
                v-if="item.kind === 'connector' || item.kind === 'template'"
                class="mt-2 text-xs text-slate-500"
              >
                {{ installsLabel(item.installs) }}<template v-if="item.ratingCount > 0">
                  · {{ item.averageRating.toFixed(1) }} ★ ({{ item.ratingCount }})
                </template>
              </p>
              <p
                v-if="item.deprecatedAt"
                class="mt-2 text-xs text-amber-700"
              >
                Deprecated {{ formatDate(item.deprecatedAt) }}: {{ item.deprecationNote ?? 'no longer maintained.' }}
              </p>
            </router-link>
          </div>
        </section>
      </template>

      <p
        v-if="profile.publishingSince"
        class="text-xs text-slate-400"
      >
        Publishing in the Flowamaz Library since {{ formatDate(profile.publishingSince) }}.
      </p>
    </template>
  </div>
</template>
