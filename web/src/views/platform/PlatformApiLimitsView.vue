<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useDebounceFn } from '@vueuse/core';
import { Building2 } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { useAuth } from '@/composables/useAuth';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { platformService, type PlatformOrgApiLimit } from '@/services/platform.service';

// Flowamaz staff: set the public API limit agreed with a customer (Enterprise "higher on request").
// The API enforces who may use this; the page only explains it to anyone else who opens the URL.

const auth = useAuth();
const toast = useToast();
const isStaff = computed(() => auth.user.value?.isPlatformStaff === true);

const loading = ref(true);
const loadError = ref('');
const orgs = ref<PlatformOrgApiLimit[]>([]);
const search = ref('');
const editing = ref<string | null>(null);
const draft = ref('');
const draftError = ref('');
const saving = ref(false);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    orgs.value = await platformService.searchOrganisations(search.value.trim() || undefined);
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  if (isStaff.value) void load();
  else loading.value = false;
});
const onSearch = useDebounceFn(load, 300);

function startEdit(org: PlatformOrgApiLimit): void {
  editing.value = org.orgId;
  draft.value = org.customPerMinute === null ? '' : String(org.customPerMinute);
  draftError.value = '';
}

/** Blank = back to the plan's limit. */
function parseDraft(): number | null | undefined {
  const text = draft.value.trim().replace(/[,\s]/g, '');
  if (text === '') return null;
  if (!/^\d+$/.test(text) || Number(text) > 100000) return undefined;
  return Number(text);
}

async function save(org: PlatformOrgApiLimit, perMinute: number | null | undefined = parseDraft()): Promise<void> {
  if (perMinute === undefined) {
    draftError.value = 'Enter a whole number from 0 to 100,000 (0 means no per-minute limit), or leave it blank to use the plan\'s limit.';
    return;
  }
  saving.value = true;
  try {
    const updated = await platformService.setApiLimit(org.orgId, perMinute);
    orgs.value = orgs.value.map((o) => (o.orgId === org.orgId ? updated : o));
    editing.value = null;
    toast.success(`${org.name} now has ${limitText(updated.effectivePerMinute)}. It applies within a minute.`);
  } catch (e) {
    draftError.value = toUserFacingError(e).message;
  } finally {
    saving.value = false;
  }
}

const limitText = (perMinute: number) => (perMinute === 0 ? 'no per-minute limit' : `${perMinute.toLocaleString()} calls a minute`);
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6 p-6">
    <div>
      <h1 class="text-xl font-semibold text-slate-900">
        API limits
      </h1>
      <p class="mt-1 text-sm text-slate-500">
        Set the public API limit agreed with a customer. It replaces their plan's limit for every workspace in the
        organisation, and they see it on their billing page. Leave it blank to go back to the plan's limit.
      </p>
    </div>

    <FmAlert
      v-if="!isStaff"
      type="info"
    >
      <p class="text-sm font-medium">
        This page is for Flowamaz staff
      </p>
      <p class="mt-1 text-xs">
        Your organisation's API limit is on Settings → Billing. To ask for a different one, email team@flowamaz.io.
      </p>
    </FmAlert>

    <template v-else>
      <div class="max-w-sm">
        <FmInput
          v-model="search"
          label="Find an organisation"
          placeholder="Name or slug"
          autocomplete="off"
          @update:model-value="onSearch"
        />
      </div>

      <div
        v-if="loading"
        class="flex justify-center py-10 text-primary-600"
      >
        <FmSpinner />
      </div>

      <FmErrorState
        v-else-if="loadError"
        title="Couldn't load organisations"
        :description="loadError"
        action-label="Try again"
        @action="load"
      />

      <p
        v-else-if="orgs.length === 0"
        class="rounded-lg bg-slate-50 p-4 text-sm text-slate-600"
        data-test="platform-orgs-empty"
      >
        No organisation's name or slug matches "{{ search.trim() }}". Check the spelling, or search by part of the slug
        (it's in the customer's Flowamaz address).
      </p>

      <ul
        v-else
        class="divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white"
      >
        <li
          v-for="o in orgs"
          :key="o.orgId"
          class="space-y-2 px-4 py-3"
          data-test="platform-org"
        >
          <div class="flex flex-wrap items-center gap-3 text-sm">
            <Building2 class="h-4 w-4 text-slate-400" />
            <span class="font-medium text-slate-800">{{ o.name }}</span>
            <span class="font-mono text-xs text-slate-500">{{ o.slug }}</span>
            <span class="text-xs text-slate-500">{{ o.planName }} plan: {{ limitText(o.planPerMinute) }}</span>
            <span
              class="text-xs"
              :class="o.customPerMinute === null ? 'text-slate-500' : 'font-medium text-primary-700'"
            >
              {{ o.customPerMinute === null ? 'Uses the plan limit' : `Agreed: ${limitText(o.customPerMinute)}` }}
            </span>
            <FmButton
              v-if="editing !== o.orgId"
              class="ml-auto"
              variant="secondary"
              :aria-label="`Change the API limit of ${o.name}`"
              @click="startEdit(o)"
            >
              Change
            </FmButton>
          </div>
          <form
            v-if="editing === o.orgId"
            class="flex flex-wrap items-end gap-2"
            @submit.prevent="save(o)"
          >
            <div class="w-56">
              <FmInput
                v-model="draft"
                label="Calls a minute"
                placeholder="Blank = plan limit"
                inputmode="numeric"
                autocomplete="off"
                :error="draftError"
              />
            </div>
            <FmButton
              type="submit"
              :loading="saving"
            >
              Save
            </FmButton>
            <FmButton
              v-if="o.customPerMinute !== null"
              variant="ghost"
              :disabled="saving"
              @click="save(o, null)"
            >
              Use plan limit
            </FmButton>
            <FmButton
              variant="ghost"
              :disabled="saving"
              @click="editing = null"
            >
              Cancel
            </FmButton>
          </form>
        </li>
      </ul>
    </template>
  </div>
</template>
