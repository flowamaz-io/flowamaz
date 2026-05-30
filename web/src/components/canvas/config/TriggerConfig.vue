<script setup lang="ts">
import { computed } from 'vue';

type Config = Record<string, unknown>;

const props = defineProps<{ config: Config; webhookUrl?: string }>();
const emit = defineEmits<{ update: [config: Config] }>();

const TRIGGER_TYPES = ['manual', 'webhook', 'schedule', 'event'] as const;

const triggerType = computed<string>(() => (props.config.triggerType as string) ?? 'manual');
const cron = computed<string>(() => (props.config.cron as string) ?? '');
const eventName = computed<string>(() => (props.config.event as string) ?? '');

function patch(updates: Config): void {
  emit('update', { ...props.config, ...updates });
}

// Lightweight human-readable preview for the most common cron shapes.
const cronPreview = computed<string>(() => {
  const parts = cron.value.trim().split(/\s+/);
  if (parts.length !== 5) return cron.value ? 'Custom schedule' : '';
  const [min, hour, dom, , dow] = parts;
  const time = hour !== '*' && min !== '*'
    ? `${hour.padStart(2, '0')}:${min.padStart(2, '0')}`
    : null;
  if (dow === '1-5' && time) return `Every weekday at ${time}`;
  if (dom === '*' && dow === '*' && time) return `Every day at ${time}`;
  if (dow !== '*' && time) return `Weekly (day ${dow}) at ${time}`;
  return 'Custom schedule';
});
</script>

<template>
  <div class="space-y-4">
    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Trigger type</label>
      <select
        :value="triggerType"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 capitalize focus:border-indigo-400 focus:outline-none"
        @change="patch({ triggerType: ($event.target as HTMLSelectElement).value })"
      >
        <option
          v-for="t in TRIGGER_TYPES"
          :key="t"
          :value="t"
        >
          {{ t }}
        </option>
      </select>
    </div>

    <div v-if="triggerType === 'webhook'">
      <label class="mb-1 block text-xs font-medium text-gray-300">Webhook URL</label>
      <code class="block break-all rounded-md border border-gray-700 bg-gray-800 px-2 py-1.5 text-xs text-gray-300">
        {{ webhookUrl || 'Create a webhook in Settings → Webhooks for this workflow.' }}
      </code>
      <RouterLink
        :to="{ name: 'webhooks' }"
        class="mt-1 inline-block text-xs text-indigo-400 hover:text-indigo-300"
      >
        Manage webhooks
      </RouterLink>
    </div>

    <div v-else-if="triggerType === 'schedule'">
      <label class="mb-1 block text-xs font-medium text-gray-300">Cron expression</label>
      <input
        :value="cron"
        type="text"
        placeholder="0 9 * * 1-5"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm font-mono text-gray-100 focus:border-indigo-400 focus:outline-none"
        @input="patch({ cron: ($event.target as HTMLInputElement).value })"
      >
      <p
        v-if="cronPreview"
        class="mt-1 text-xs text-emerald-400"
      >
        {{ cronPreview }}
      </p>
    </div>

    <div v-else-if="triggerType === 'event'">
      <label class="mb-1 block text-xs font-medium text-gray-300">Event name</label>
      <input
        :value="eventName"
        type="text"
        placeholder="order.created"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @input="patch({ event: ($event.target as HTMLInputElement).value })"
      >
    </div>

    <p
      v-else
      class="text-xs text-gray-500"
    >
      Manual triggers are started by hand or via the API — no extra configuration needed.
    </p>
  </div>
</template>
