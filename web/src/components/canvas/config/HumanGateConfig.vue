<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { credentialService, type CredentialAlias } from '@/services/credential.service';
import { Plus, Trash2 } from 'lucide-vue-next';
import {
  type Config, hasKey, readNumber, readString, withoutKeys, writeAlias,
} from './configKeys';

interface GateOption {
  label: string;
  value: string;
}

const props = defineProps<{ config: Config; workspaceId?: string }>();
const emit = defineEmits<{ update: [config: Config] }>();

const ASSIGNEE_TYPES = ['role', 'user', 'variable'] as const;
const ROLES = ['Admin', 'Designer', 'Operator', 'Viewer'] as const;
const CHANNELS = [
  { key: 'email', label: 'Email' },
  { key: 'slack', label: 'Slack' },
  { key: 'teams', label: 'Teams' },
] as const;
type ChannelKey = (typeof CHANNELS)[number]['key'];

// Key aliases in the order HumanGateNodeWorker reads them (the snake_case spec key wins).
const KEYS = {
  slackChannel: ['slack_channel', 'slackChannel'],
  slackCredential: ['slack_credential', 'slackCredential'],
  teamsWebhookUrl: ['teams_webhook_url', 'teamsWebhookUrl'],
  onTimeout: ['on_timeout', 'onTimeout'],
  escalateTo: ['escalate_to', 'escalateTo'],
} as const;

/** True when the gate was written with flowamaz/v1 spec keys (snake_case) rather than canvas keys. */
const specStyle = computed(() => Object.keys(props.config).some((k) => k.includes('_')));

/** The engine's decision window when no timeout is configured (HumanGateNodeWorker.DefaultTimeoutHours). */
const DEFAULT_TIMEOUT_HOURS = 48;

// ── Assignee ────────────────────────────────────────────────────────────────
// The engine resolves `assigned_to_variable` before assigneeType/assignee.
const assigneeType = computed<string>(() => {
  if (hasKey(props.config, 'assigned_to_variable')) return 'variable';
  if (hasKey(props.config, 'assigned_role')) return 'role';
  return readString(props.config, ['assigneeType'])?.toLowerCase() ?? 'role';
});
const assignee = computed<string>(() => {
  if (hasKey(props.config, 'assigned_to_variable')) return readString(props.config, ['assigned_to_variable']) ?? '';
  if (hasKey(props.config, 'assigned_role')) return readString(props.config, ['assigned_role']) ?? '';
  return readString(props.config, ['assignee']) ?? '';
});

function setAssigneeType(type: string): void {
  if (type === assigneeType.value) return;
  emit('update', {
    ...withoutKeys(props.config, ['assigned_to_variable', 'assigned_role']),
    assigneeType: type,
    assignee: '',
  });
}

function setAssignee(value: string): void {
  if (hasKey(props.config, 'assigned_to_variable')) {
    emit('update', { ...props.config, assigned_to_variable: value });
  } else if (hasKey(props.config, 'assigned_role')) {
    emit('update', { ...props.config, assigned_role: value });
  } else {
    emit('update', { ...props.config, assignee: value });
  }
}

// ── Decision options ────────────────────────────────────────────────────────
const options = computed<GateOption[]>(
  () => (props.config.options as GateOption[]) ?? [
    { label: 'Approve', value: 'approve' },
    { label: 'Reject', value: 'reject' },
  ],
);

// ── Channels ────────────────────────────────────────────────────────────────
// `delivery_channel` (Portal | Email | Slack | Teams) wins over the canvas `channels` map.
// With neither set the engine delivers to the portal only, so nothing is pre-ticked.
const channels = computed<Record<string, boolean>>(() => {
  const delivery = readString(props.config, ['delivery_channel']);
  if (delivery !== undefined) {
    const d = delivery.trim().toLowerCase();
    return { email: d === 'email', slack: d === 'slack', teams: d === 'teams' };
  }
  const map = props.config.channels;
  return map && typeof map === 'object' ? (map as Record<string, boolean>) : {};
});

function toggleChannel(key: ChannelKey): void {
  const next = { ...channels.value, [key]: !channels.value[key] };
  if (hasKey(props.config, 'delivery_channel')) {
    // Keep the spec key while it can express the choice (one channel); several channels need
    // the canvas map, which the engine reads once delivery_channel is gone.
    const enabled = CHANNELS.filter((c) => next[c.key]);
    if (enabled.length <= 1) {
      emit('update', { ...props.config, delivery_channel: enabled[0]?.label ?? 'Portal' });
    } else {
      emit('update', { ...withoutKeys(props.config, ['delivery_channel']), channels: next });
    }
    return;
  }
  emit('update', { ...props.config, channels: next });
}

const slackChannel = computed<string>(() => readString(props.config, KEYS.slackChannel) ?? '');
const slackCredential = computed<string>(() => readString(props.config, KEYS.slackCredential) ?? '');
const teamsWebhookUrl = computed<string>(() => readString(props.config, KEYS.teamsWebhookUrl) ?? '');

// ── Timeout ─────────────────────────────────────────────────────────────────
/** Effective window in hours, resolved like HumanGateNodeWorker: timeout_hours, then canvas value/unit, then default. */
const timeoutHours = computed<number>(() => {
  const spec = readNumber(props.config, ['timeout_hours']);
  if (spec !== undefined) return spec;
  const value = readNumber(props.config, ['timeoutValue']);
  if (value !== undefined && value > 0) {
    return readString(props.config, ['timeoutUnit'])?.toLowerCase() === 'days' ? value * 24 : value;
  }
  return DEFAULT_TIMEOUT_HOURS;
});

// The unit is a display choice; remember the user's pick so the field doesn't flip while typing.
const unitOverride = ref<'hours' | 'days' | null>(null);
const timeoutUnit = computed<'hours' | 'days'>(() => {
  if (unitOverride.value) return unitOverride.value;
  if (!hasKey(props.config, 'timeout_hours') && hasKey(props.config, 'timeoutValue')) {
    return readString(props.config, ['timeoutUnit'])?.toLowerCase() === 'days' ? 'days' : 'hours';
  }
  const h = timeoutHours.value;
  return h >= 24 && h % 24 === 0 ? 'days' : 'hours';
});
const timeoutValue = computed<number>(() =>
  timeoutUnit.value === 'days' ? timeoutHours.value / 24 : timeoutHours.value,
);

function writeTimeout(value: number, unit: 'hours' | 'days'): void {
  if (!Number.isFinite(value) || value <= 0) return;
  const useSpecKey = hasKey(props.config, 'timeout_hours')
    || (specStyle.value && !hasKey(props.config, 'timeoutValue'));
  if (useSpecKey) {
    emit('update', {
      ...withoutKeys(props.config, ['timeoutValue', 'timeoutUnit']),
      timeout_hours: Math.round(unit === 'days' ? value * 24 : value),
    });
  } else {
    emit('update', { ...props.config, timeoutValue: value, timeoutUnit: unit });
  }
}

function setTimeoutValue(raw: string): void {
  writeTimeout(Number(raw), timeoutUnit.value);
}

function setTimeoutUnit(unit: string): void {
  const u = unit === 'days' ? 'days' : 'hours';
  const value = timeoutValue.value;
  unitOverride.value = u;
  writeTimeout(value, u);
}

/** Same normalisation as GateTimeoutActions.Normalize — unknown values fail the run. */
function normalizeOnTimeout(value: string | undefined): string {
  switch (value?.trim().toLowerCase().replace(/_/g, '-')) {
    case 'escalate': return 'escalate';
    case 'auto-approve':
    case 'approve': return 'auto-approve';
    case 'auto-reject':
    case 'reject': return 'auto-reject';
    default: return 'fail';
  }
}
const onTimeout = computed<string>(() => normalizeOnTimeout(readString(props.config, KEYS.onTimeout)));
const escalateTo = computed<string>(() => readString(props.config, KEYS.escalateTo) ?? '');

const slackCredentials = ref<CredentialAlias[]>([]);
async function loadSlackCredentials(): Promise<void> {
  if (!props.workspaceId || !channels.value.slack) return;
  try {
    slackCredentials.value = (await credentialService.list(props.workspaceId, 'slack')).filter((c) => c.isActive);
  } catch {
    slackCredentials.value = [];
  }
}
onMounted(loadSlackCredentials);
watch(() => channels.value.slack, loadSlackCredentials);

const escalateToInvalid = computed(
  () => onTimeout.value === 'escalate' && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(escalateTo.value.trim()),
);

function patch(updates: Config): void {
  emit('update', { ...props.config, ...updates });
}

/**
 * Store a setting under the alias already in use (spec key or canvas key). A setting the node
 * doesn't have yet follows the node's style: spec keys for YAML-authored gates, canvas keys otherwise.
 */
function patchAlias(keys: readonly string[], fallbackKey: string, value: unknown): void {
  emit('update', writeAlias(props.config, keys, specStyle.value ? keys[0]! : fallbackKey, value));
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
</script>

<template>
  <div class="space-y-4">
    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Assignee type</label>
      <select
        data-test="gate-assignee-type"
        :value="assigneeType"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm capitalize text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="setAssigneeType(($event.target as HTMLSelectElement).value)"
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
        data-test="gate-assignee"
        :value="assignee"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="setAssignee(($event.target as HTMLSelectElement).value)"
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
        data-test="gate-assignee"
        :value="assignee"
        type="text"
        :placeholder="assigneeType === 'user' ? 'Search members by email' : 'approver_email (a workflow variable)'"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @input="setAssignee(($event.target as HTMLInputElement).value)"
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
            :data-test="`gate-channel-${channel.key}`"
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

      <div
        v-if="channels.slack"
        class="mt-3 space-y-2"
      >
        <div>
          <label class="mb-1 block text-xs font-medium text-gray-300">Slack channel</label>
          <input
            :value="slackChannel"
            placeholder="#approvals"
            class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
            @input="patchAlias(KEYS.slackChannel, 'slackChannel', ($event.target as HTMLInputElement).value)"
          >
        </div>
        <div>
          <label class="mb-1 block text-xs font-medium text-gray-300">Slack account</label>
          <select
            :value="slackCredential"
            class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
            @change="patchAlias(KEYS.slackCredential, 'slackCredential', ($event.target as HTMLSelectElement).value || undefined)"
          >
            <option value="">
              {{ slackCredentials.length === 1 ? `Workspace default (${slackCredentials[0]!.name})` : 'Workspace default' }}
            </option>
            <option
              v-for="c in slackCredentials"
              :key="c.id"
              :value="c.name"
            >
              {{ c.name }}
            </option>
          </select>
          <p
            v-if="workspaceId && slackCredentials.length === 0"
            class="mt-1 text-xs text-amber-400"
          >
            Slack isn't connected —
            <RouterLink
              to="/settings/credentials"
              class="underline hover:text-amber-300"
            >
              connect it in Settings → Credentials
            </RouterLink>; until then this approval only appears in the portal.
          </p>
        </div>
      </div>

      <div
        v-if="channels.teams"
        class="mt-3"
      >
        <label class="mb-1 block text-xs font-medium text-gray-300">Teams incoming webhook URL</label>
        <input
          :value="teamsWebhookUrl"
          type="url"
          placeholder="https://…/workflows/…"
          class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
          @input="patchAlias(KEYS.teamsWebhookUrl, 'teamsWebhookUrl', ($event.target as HTMLInputElement).value)"
        >
        <p class="mt-1 text-xs text-gray-500">
          Create one in the Teams channel (Workflows → "Post to a channel when a webhook request is received").
        </p>
      </div>
    </div>

    <div>
      <label class="mb-1 block text-xs font-medium text-gray-300">Timeout</label>
      <div class="flex items-center gap-2">
        <input
          data-test="gate-timeout-value"
          :value="timeoutValue"
          type="number"
          min="1"
          class="w-20 rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
          @input="setTimeoutValue(($event.target as HTMLInputElement).value)"
        >
        <select
          data-test="gate-timeout-unit"
          :value="timeoutUnit"
          class="rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
          @change="setTimeoutUnit(($event.target as HTMLSelectElement).value)"
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
        data-test="gate-on-timeout"
        :value="onTimeout"
        class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
        @change="patchAlias(KEYS.onTimeout, 'onTimeout', ($event.target as HTMLSelectElement).value)"
      >
        <option value="fail">
          Fail the run
        </option>
        <option value="escalate">
          Escalate to someone else
        </option>
        <option value="auto-approve">
          Auto-approve
        </option>
        <option value="auto-reject">
          Auto-reject
        </option>
      </select>
      <div
        v-if="onTimeout === 'escalate'"
        class="mt-2"
      >
        <label class="mb-1 block text-xs font-medium text-gray-300">Escalate to (email)</label>
        <input
          :value="escalateTo"
          type="email"
          data-test="gate-escalate-to"
          placeholder="director@example.com"
          :aria-invalid="escalateToInvalid"
          class="w-full rounded-md border border-gray-600 bg-gray-800 px-2 py-1.5 text-sm text-gray-100 focus:border-indigo-400 focus:outline-none"
          @input="patchAlias(KEYS.escalateTo, 'escalateTo', ($event.target as HTMLInputElement).value)"
        >
        <p
          v-if="escalateToInvalid && escalateTo.trim() === ''"
          data-test="gate-escalate-error"
          class="mt-1 text-xs text-amber-400"
        >
          Enter who should get the approval if nobody decides in time — without it, the run fails at the deadline.
        </p>
        <p
          v-else-if="escalateToInvalid"
          data-test="gate-escalate-error"
          class="mt-1 text-xs text-amber-400"
        >
          "{{ escalateTo.trim() }}" isn't a valid email address. Enter one like director@example.com —
          otherwise nobody can be escalated to and the run fails at the deadline.
        </p>
        <p
          v-else
          class="mt-1 text-xs text-gray-500"
        >
          They get a new window of the same length. If they don't decide either, the run fails.
        </p>
      </div>
    </div>
  </div>
</template>
