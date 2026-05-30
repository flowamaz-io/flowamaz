<script setup lang="ts">
import { computed } from 'vue';
import { Plus, Trash2 } from 'lucide-vue-next';

type Config = Record<string, unknown>;

interface GateOption {
  label: string;
  value: string;
}

const props = defineProps<{ config: Config }>();
const emit = defineEmits<{ update: [config: Config] }>();

const ASSIGNEE_TYPES = ['role', 'user', 'variable'] as const;
const ROLES = ['Admin', 'Designer', 'Operator', 'Viewer'] as const;
const CHANNELS = [
  { key: 'email', label: 'Email' },
  { key: 'slack', label: 'Slack' },
  { key: 'teams', label: 'Teams' },
] as const;

const assigneeType = computed<string>(() => (props.config.assigneeType as string) ?? 'role');
const assignee = computed<string>(() => (props.config.assignee as string) ?? '');
const options = computed<GateOption[]>(
  () => (props.config.options as GateOption[]) ?? [
    { label: 'Approve', value: 'approve' },
    { label: 'Reject', value: 'reject' },
  ],
);
const channels = computed<Record<string, boolean>>(
  () => (props.config.channels as Record<string, boolean>) ?? { email: true },
);
const timeoutValue = computed<number>(() => (props.config.timeoutValue as number) ?? 24);
const timeoutUnit = computed<string>(() => (props.config.timeoutUnit as string) ?? 'hours');
const onTimeout = computed<string>(() => (props.config.onTimeout as string) ?? 'escalate');

function patch(updates: Config): void {
  emit('update', { ...props.config, ...updates });
}

function setOption(index: number, label: string): void {
  patch({
    options: options.value.map((o, i) =>
      i === index ? { label, value: label.toLowerCase().replace(/\s+/g, '_') } : o,
    ),
  });
}

function addOption(): void {
  patch({ options: [...options.value, { label: 'New option', value: 'new_option' }] });
}

function removeOption(index: number): void {
  patch({ options: options.value.filter((_, i) => i !== index) });
}

function toggleChannel(key: string): void {
  patch({ channels: { ...channels.value, [key]: !channels.value[key] } });
}
</script>

<template>
  <div class="space-y-4">
    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Assignee type</label>
      <select
        :value="assigneeType"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm capitalize text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ assigneeType: ($event.target as HTMLSelectElement).value, assignee: '' })"
      >
        <option
          v-for="t in ASSIGNEE_TYPES"
          :key="t"
          :value="t"
        >
          {{ t === 'user' ? 'Specific user' : t }}
        </option>
      </select>
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">
        {{ assigneeType === 'role' ? 'Role' : assigneeType === 'user' ? 'User' : 'Variable' }}
      </label>
      <select
        v-if="assigneeType === 'role'"
        :value="assignee"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ assignee: ($event.target as HTMLSelectElement).value })"
      >
        <option value="">
          Select a role
        </option>
        <option
          v-for="r in ROLES"
          :key="r"
          :value="r"
        >
          {{ r }}
        </option>
      </select>
      <input
        v-else
        :value="assignee"
        type="text"
        :placeholder="assigneeType === 'user' ? 'Search members by email' : '${approver_email}'"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @input="patch({ assignee: ($event.target as HTMLInputElement).value })"
      >
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Decision options</label>
      <div class="space-y-2">
        <div
          v-for="(option, index) in options"
          :key="index"
          class="flex items-center gap-2"
        >
          <input
            :value="option.label"
            type="text"
            class="flex-1 rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
            @input="setOption(index, ($event.target as HTMLInputElement).value)"
          >
          <button
            type="button"
            class="rounded p-1 text-gray-500 hover:bg-gray-700 hover:text-red-400"
            aria-label="Remove option"
            @click="removeOption(index)"
          >
            <Trash2 class="h-4 w-4" />
          </button>
        </div>
      </div>
      <button
        type="button"
        class="mt-2 flex items-center gap-1 text-xs font-medium text-indigo-400 hover:text-indigo-300"
        @click="addOption"
      >
        <Plus class="h-3.5 w-3.5" />
        Add option
      </button>
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Notification channels</label>
      <div class="flex flex-wrap gap-3">
        <label
          v-for="channel in CHANNELS"
          :key="channel.key"
          class="flex items-center gap-1.5 text-sm text-gray-200"
        >
          <input
            type="checkbox"
            :checked="!!channels[channel.key]"
            class="h-4 w-4 rounded border-gray-600 bg-gray-800 text-indigo-500"
            @change="toggleChannel(channel.key)"
          >
          {{ channel.label }}
        </label>
        <span class="flex items-center gap-1.5 text-sm text-gray-500">
          <input
            type="checkbox"
            checked
            disabled
            class="h-4 w-4 rounded border-gray-600 bg-gray-800"
          >
          Portal (always on)
        </span>
      </div>
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Timeout</label>
      <div class="flex items-center gap-2">
        <input
          :value="timeoutValue"
          type="number"
          min="1"
          class="w-20 rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
          @input="patch({ timeoutValue: Number(($event.target as HTMLInputElement).value) })"
        >
        <select
          :value="timeoutUnit"
          class="rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
          @change="patch({ timeoutUnit: ($event.target as HTMLSelectElement).value })"
        >
          <option value="hours">
            hours
          </option>
          <option value="days">
            days
          </option>
        </select>
      </div>
      <label class="mb-1 mt-2 block text-xs font-medium text-gray-300">On timeout</label>
      <select
        :value="onTimeout"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patch({ onTimeout: ($event.target as HTMLSelectElement).value })"
      >
        <option value="escalate">
          Escalate to admin
        </option>
        <option value="auto-approve">
          Auto-approve
        </option>
        <option value="auto-reject">
          Auto-reject
        </option>
      </select>
    </div>
  </div>
</template>
