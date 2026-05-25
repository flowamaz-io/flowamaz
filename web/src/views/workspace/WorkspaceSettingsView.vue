<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import {
  AI_FUNCTIONS,
  KEY_SOURCES,
  MARKETPLACE_POLICIES,
  PROVIDER_LABELS,
  PROVIDER_MODELS,
  RETENTION_OPTIONS,
} from '@/utils/constants';
import type { FunctionOverrideRequest, MarketplacePolicy } from '@/types';

const ws = useWorkspace();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const savingSettings = ref(false);
const savingAi = ref(false);

const name = ref('');
const slug = ref('');
const retentionDays = ref(7);
const marketplacePolicy = ref<MarketplacePolicy>('OfficialAndVerified');

// Per-function AI overrides keyed by functionId.
interface FnState {
  provider: string;
  modelId: string;
  keySource: string;
  resolvedFrom: string;
}
const fnState = ref<Record<string, FnState>>({});

function modelsFor(provider: string): string[] {
  return PROVIDER_MODELS[provider] ?? [];
}

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    await ws.loadCurrentWorkspace();
    await ws.loadAiConfig();
    const wsData = ws.currentWorkspace.value;
    if (wsData) {
      name.value = wsData.name;
      slug.value = wsData.slug;
      retentionDays.value = wsData.settings.runRetentionDays;
      marketplacePolicy.value = wsData.settings.marketplacePolicy;
    }
    const cfg = ws.aiConfig.value;
    if (cfg) {
      const next: Record<string, FnState> = {};
      for (const fn of AI_FUNCTIONS) {
        const resolved = cfg.functions.find((f) => f.functionId === fn.id);
        next[fn.id] = {
          provider: resolved?.provider ?? 'anthropic',
          modelId: resolved?.modelId ?? '',
          keySource: 'Platform',
          resolvedFrom: resolved?.resolvedFrom ?? 'platform',
        };
      }
      fnState.value = next;
    }
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);
watch(() => ws.currentWorkspaceId.value, load);

async function saveSettings(): Promise<void> {
  const wsData = ws.currentWorkspace.value;
  if (!wsData) return;
  savingSettings.value = true;
  try {
    await ws.updateSettings({
      maxConcurrentRuns: wsData.settings.maxConcurrentRuns,
      runRetentionDays: retentionDays.value,
      aiCostBudgetMonthUsd: wsData.settings.aiCostBudgetMonthUsd,
      allowedAiProviders: wsData.settings.allowedAiProviders,
      defaultAiModelOverrides: wsData.settings.defaultAiModelOverrides,
      marketplacePolicy: marketplacePolicy.value,
    });
    name.value = ws.currentWorkspace.value?.name ?? name.value;
    toast.success('Workspace settings saved.');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    savingSettings.value = false;
  }
}

async function saveAiConfig(): Promise<void> {
  savingAi.value = true;
  try {
    const overrides: FunctionOverrideRequest[] = AI_FUNCTIONS.filter(
      (fn) => fnState.value[fn.id]?.modelId,
    ).map((fn) => {
      const s = fnState.value[fn.id]!;
      return { functionId: fn.id, provider: s.provider, modelId: s.modelId, keySource: s.keySource };
    });
    await ws.updateAiConfig({ overrides });
    await load();
    toast.success('AI model configuration saved.');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    savingAi.value = false;
  }
}

const allowedProviders = (): string[] => ws.aiConfig.value?.allowedProviders ?? ['anthropic'];
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-6 p-6">
    <header>
      <h1 class="text-2xl font-semibold text-slate-900">
        Workspace settings
      </h1>
      <p class="text-sm text-slate-500">
        Configure your workspace name, retention, and AI models.
      </p>
    </header>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load this workspace"
      :description="loadError"
      action-label="Try again"
      help-article="workspaces/what-is-a-workspace"
      @action="load"
    />

    <template v-else>
      <!-- General -->
      <section class="rounded-xl border border-slate-200 bg-white p-6">
        <h2 class="mb-4 text-base font-semibold text-slate-900">
          General
        </h2>
        <div class="space-y-4">
          <FmInput
            v-model="name"
            label="Workspace name"
            disabled
            hint="Name editing arrives with workspace admin tools."
          />
          <FmInput
            v-model="slug"
            label="Workspace URL"
            prefix="flowamaz.io/"
            disabled
            hint="The slug is fixed after creation."
          />
          <div>
            <label class="mb-1 block text-sm font-medium text-slate-700">Run retention</label>
            <select
              v-model.number="retentionDays"
              class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
            >
              <option
                v-for="d in RETENTION_OPTIONS"
                :key="d"
                :value="d"
              >
                {{ d }} days
              </option>
            </select>
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium text-slate-700">Marketplace policy</label>
            <select
              v-model="marketplacePolicy"
              class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
            >
              <option
                v-for="p in MARKETPLACE_POLICIES"
                :key="p.value"
                :value="p.value"
              >
                {{ p.label }}
              </option>
            </select>
          </div>
        </div>
        <div class="mt-5 flex justify-end">
          <FmButton
            :loading="savingSettings"
            @click="saveSettings"
          >
            Save settings
          </FmButton>
        </div>
      </section>

      <!-- AI model config -->
      <section class="rounded-xl border border-slate-200 bg-white p-6">
        <h2 class="text-base font-semibold text-slate-900">
          AI model configuration
        </h2>
        <p class="mb-4 text-sm text-slate-500">
          Choose the provider and model for each of the seven platform AI functions. Defaults resolve from your
          organisation or the platform.
        </p>
        <FmAlert
          type="info"
          class="mb-4"
        >
          Models are limited to your organisation's allowed providers: {{ allowedProviders().join(', ') }}.
        </FmAlert>

        <div class="space-y-3">
          <div
            v-for="fn in AI_FUNCTIONS"
            :key="fn.id"
            class="flex flex-col gap-2 rounded-lg border border-slate-100 p-3 md:flex-row md:items-center"
          >
            <div class="md:w-48">
              <p class="text-sm font-medium text-slate-800">
                {{ fn.label }}
              </p>
              <p class="text-xs text-slate-400">
                {{ fn.description }}
              </p>
            </div>
            <div
              v-if="fnState[fn.id]"
              class="grid flex-1 grid-cols-1 gap-2 sm:grid-cols-3"
            >
              <select
                v-model="fnState[fn.id]!.provider"
                class="rounded-lg border border-slate-300 bg-white px-2 py-1.5 text-sm focus:border-primary-500 focus:outline-none"
              >
                <option
                  v-for="p in allowedProviders()"
                  :key="p"
                  :value="p"
                >
                  {{ PROVIDER_LABELS[p] ?? p }}
                </option>
              </select>
              <select
                v-model="fnState[fn.id]!.modelId"
                class="rounded-lg border border-slate-300 bg-white px-2 py-1.5 text-sm focus:border-primary-500 focus:outline-none"
              >
                <option value="">
                  Use default
                </option>
                <option
                  v-for="model in modelsFor(fnState[fn.id]!.provider)"
                  :key="model"
                  :value="model"
                >
                  {{ model }}
                </option>
              </select>
              <select
                v-model="fnState[fn.id]!.keySource"
                class="rounded-lg border border-slate-300 bg-white px-2 py-1.5 text-sm focus:border-primary-500 focus:outline-none"
              >
                <option
                  v-for="k in KEY_SOURCES"
                  :key="k.value"
                  :value="k.value"
                >
                  {{ k.label }}
                </option>
              </select>
            </div>
            <FmBadge
              v-if="fnState[fn.id]"
              variant="slate"
              class="md:w-32 md:justify-center"
            >
              From: {{ fnState[fn.id]!.resolvedFrom }}
            </FmBadge>
          </div>
        </div>

        <div class="mt-5 flex justify-end">
          <FmButton
            :loading="savingAi"
            @click="saveAiConfig"
          >
            Save AI config
          </FmButton>
        </div>
      </section>
    </template>
  </div>
</template>
