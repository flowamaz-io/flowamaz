<script setup lang="ts">
import { computed } from 'vue';
import { useRouter } from 'vue-router';
import { formatInstalls, type ConnectorDefinition } from '@/services/connector.service';
import { CONNECTOR_INSTALL_ROLE_NEEDED } from '@/utils/constants';
import { noticeDate, type LibraryConnectorItem, type LibraryOutdatedItem } from '@/services/library.service';

interface Props {
  connector: ConnectorDefinition;
  isInstalled: boolean;
  /** This workspace's view: installed version and pin, deprecation notice. */
  library?: LibraryConnectorItem;
  /** Set when an installed connector has a newer major to upgrade to. */
  outdated?: LibraryOutdatedItem;
  /** False for members below Designer: the server refuses their installs, so no Install button. */
  canInstall?: boolean;
}

const props = withDefaults(defineProps<Props>(), { canInstall: true });
const emit = defineEmits<{ install: [connectorId: string] }>();
const router = useRouter();

const categoryEmoji: Record<string, string> = {
  finance: '💰',
  messaging: '💬',
  database: '🗄️',
  devops: '🔧',
  productivity: '📅',
  hr: '👥',
  engineering: '⚙️',
  ai: '🤖',
};

const icon = computed(
  () => categoryEmoji[props.connector.category.toLowerCase()] ?? '🔌',
);

const shortDescription = computed<string>(() => {
  try {
    const manifest = JSON.parse(props.connector.manifestJson) as { description?: string };
    const desc = manifest.description ?? props.connector.displayName;
    return desc.length > 80 ? desc.slice(0, 77) + '...' : desc;
  } catch {
    return props.connector.displayName;
  }
});

const tierClasses = computed<string>(() => {
  const map: Record<string, string> = {
    Official: 'bg-blue-100 text-blue-700',
    Community: 'bg-green-100 text-green-700',
    Verified: 'bg-purple-100 text-purple-700',
    Marketplace: 'bg-amber-100 text-amber-700',
  };
  return map[props.connector.tier] ?? 'bg-gray-100 text-gray-600';
});

const installLabel = computed<string>(() => formatInstalls(props.connector.installCount));
const ratingLabel = computed<string>(() =>
  props.connector.ratingCount > 0
    ? `${props.connector.averageRating.toFixed(1)} (${props.connector.ratingCount})`
    : 'No ratings',
);

function onInstall(): void {
  emit('install', props.connector.connectorId);
}

function onCardClick(): void {
  void router.push('/library/connectors/' + props.connector.connectorId);
}
</script>

<template>
  <div
    class="flex cursor-pointer flex-col gap-3 rounded-xl border border-slate-200 bg-white p-4 transition-shadow hover:shadow-md"
    @click="onCardClick"
  >
    <!-- Icon + tier -->
    <div class="flex items-start justify-between">
      <span class="text-2xl leading-none">{{ icon }}</span>
      <div class="flex items-center gap-1">
        <span
          v-if="!connector.isOfficial"
          class="rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700"
        >
          Community
        </span>
        <span :class="['rounded-full px-2 py-0.5 text-xs font-medium', tierClasses]">
          {{ connector.tier }}
        </span>
      </div>
    </div>

    <!-- Name + description -->
    <div>
      <p class="font-semibold text-slate-900">
        {{ connector.displayName }}
      </p>
      <router-link
        :to="`/library/publishers/${connector.publisherId}`"
        class="text-xs text-slate-500 hover:text-indigo-700 hover:underline"
        data-testid="connector-publisher-link"
        @click.stop
      >
        by {{ connector.publisherId }}
      </router-link>
      <p class="mt-1 text-sm text-slate-500 leading-snug">
        {{ shortDescription }}
      </p>
    </div>

    <!-- Category + version -->
    <div class="flex flex-wrap items-center gap-1.5">
      <span class="w-fit rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-600 capitalize">
        {{ connector.category }}
      </span>
      <span
        class="rounded-full bg-gray-100 px-2 py-0.5 font-mono text-xs text-gray-600"
        data-test="connector-version"
      >
        v{{ library?.installedVersion ?? library?.version ?? connector.version }}
      </span>
      <span
        v-if="outdated?.latestVersion"
        class="rounded-full bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700"
        data-test="connector-update-available"
      >
        v{{ outdated.latestVersion }} available
      </span>
    </div>

    <p
      v-if="library?.deprecatedAt"
      class="rounded-lg bg-amber-50 px-2 py-1 text-xs text-amber-800"
      data-test="connector-deprecated"
    >
      Deprecated. Leaves the Library on {{ noticeDate(library.removalAt) }}.
    </p>

    <!-- Footer: rating + installs + action -->
    <div class="flex items-center justify-between mt-auto pt-1">
      <div class="flex items-center gap-3 text-xs text-slate-400">
        <span>⭐ {{ ratingLabel }}</span>
        <span>{{ installLabel }} installs</span>
      </div>

      <span
        v-if="!isInstalled && !canInstall"
        class="text-xs text-slate-400"
        :title="CONNECTOR_INSTALL_ROLE_NEEDED"
        data-testid="connector-install-role-note"
      >
        Designer role needed
      </span>
      <button
        v-else-if="!isInstalled"
        class="rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
        @click.stop="onInstall"
      >
        Install
      </button>
      <span
        v-else
        class="rounded-lg bg-gray-100 px-3 py-1.5 text-xs font-semibold text-gray-500"
      >
        ✓ Installed
      </span>
    </div>
  </div>
</template>
