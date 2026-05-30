<script setup lang="ts">
import { computed } from 'vue';
import { useRouter } from 'vue-router';
import type { ConnectorDefinition } from '@/services/connector.service';

interface Props {
  connector: ConnectorDefinition;
  isInstalled: boolean;
}

const props = defineProps<Props>();
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

// Static mock install counts seeded per connector (deterministic from connectorId).
const installCount = computed<string>(() => {
  const hash = props.connector.connectorId
    .split('')
    .reduce((acc, c) => acc + c.charCodeAt(0), 0);
  const count = 1000 + (hash % 1001);
  return count >= 1000 ? (count / 1000).toFixed(1) + 'k' : String(count);
});

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
      <span :class="['rounded-full px-2 py-0.5 text-xs font-medium', tierClasses]">
        {{ connector.tier }}
      </span>
    </div>

    <!-- Name + description -->
    <div>
      <p class="font-semibold text-slate-900">
        {{ connector.displayName }}
      </p>
      <p class="mt-1 text-sm text-slate-500 leading-snug">
        {{ shortDescription }}
      </p>
    </div>

    <!-- Category -->
    <span class="w-fit rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-600 capitalize">
      {{ connector.category }}
    </span>

    <!-- Footer: rating + installs + action -->
    <div class="flex items-center justify-between mt-auto pt-1">
      <div class="flex items-center gap-3 text-xs text-slate-400">
        <span>⭐ 4.5</span>
        <span>{{ installCount }} installs</span>
      </div>

      <button
        v-if="!isInstalled"
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
