<script setup lang="ts">
import { computed, ref } from 'vue';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { useAuth } from '@/composables/useAuth';
import { useOnboarding } from '@/composables/useOnboarding';
import { useToast } from '@/composables/useToast';
import { useAuthStore } from '@/stores/auth.store';
import { slugify } from '@/utils/string.util';
import { toUserFacingError } from '@/utils/error.util';
import { CREATION_METHODS } from '@/utils/constants';
import type { WorkspaceRole } from '@/types';

const emit = defineEmits<{ finished: [] }>();

const ws = useWorkspace();
const { user } = useAuth();
const toast = useToast();

const orgId = computed(() => user.value?.orgId ?? 'anon');
const onboarding = useOnboarding(orgId.value);
const step = computed(() => onboarding.state.value.step);

// Step 1 — workspace
const workspaceName = ref('');
const workspaceSlug = ref('');
const slugTouched = ref(false);
const submitting = ref(false);
const error = ref('');

function onNameInput(value: string): void {
  workspaceName.value = value;
  if (!slugTouched.value) workspaceSlug.value = slugify(value);
}

async function createWorkspaceAndAdvance(): Promise<void> {
  error.value = '';
  if (workspaceName.value.trim().length < 2) {
    error.value = 'Give your workspace a name of at least 2 characters so your team can recognise it.';
    return;
  }
  submitting.value = true;
  try {
    await ws.createWorkspace({ name: workspaceName.value.trim(), slug: workspaceSlug.value });
    // Refresh the JWT so the new workspace membership claim is included, then reload
    // the workspace list so the selector in the app shell reflects it immediately.
    const authStore = useAuthStore();
    await authStore.refreshToken();
    await ws.loadWorkspaces();
    onboarding.markWorkspaceCreated();
    onboarding.setStep(2);
  } catch (e) {
    error.value = toUserFacingError(e).message;
  } finally {
    submitting.value = false;
  }
}

// Step 3 — invite
const inviteEmail = ref('');
const inviteRole = ref<WorkspaceRole>('Designer');
const inviteRoles: WorkspaceRole[] = ['Designer', 'Operator', 'Viewer'];

async function finish(): Promise<void> {
  error.value = '';
  if (inviteEmail.value.trim().length > 0) {
    submitting.value = true;
    try {
      await ws.inviteMember({ email: inviteEmail.value.trim(), role: inviteRole.value });
      onboarding.markMemberInvited();
      toast.success(`Invitation sent to ${inviteEmail.value.trim()}.`);
    } catch (e) {
      error.value = toUserFacingError(e).message;
      submitting.value = false;
      return;
    }
    submitting.value = false;
  }
  onboarding.complete();
  emit('finished');
}

function skipInvite(): void {
  onboarding.complete();
  emit('finished');
}
</script>

<template>
  <div class="mx-auto w-full max-w-xl">
    <!-- progress -->
    <div class="mb-8 flex items-center justify-center gap-2">
      <span
        v-for="n in 3"
        :key="n"
        :class="['h-1.5 w-12 rounded-full', n <= step ? 'bg-primary-600' : 'bg-slate-200']"
      />
    </div>

    <FmAlert
      v-if="error"
      type="error"
      class="mb-4"
    >
      {{ error }}
    </FmAlert>

    <!-- Step 1 -->
    <div v-if="step === 1">
      <h1 class="text-2xl font-semibold text-slate-900">
        Name your first workspace
      </h1>
      <p class="mt-1 text-slate-500">
        A workspace holds your workflows, team, and credentials. You can create more later.
      </p>
      <div class="mt-6 space-y-4">
        <FmInput
          :model-value="workspaceName"
          label="Workspace name"
          placeholder="e.g. Operations"
          help="The display name your team sees. Used to generate the URL slug."
          @update:model-value="onNameInput"
        />
        <FmInput
          v-model="workspaceSlug"
          label="Workspace URL"
          prefix="flowamaz.io/"
          hint="Auto-generated from the name. You can edit it."
          @update:model-value="slugTouched = true"
        />
      </div>
      <div class="mt-8 flex justify-end">
        <FmButton
          :loading="submitting"
          @click="createWorkspaceAndAdvance"
        >
          Continue
        </FmButton>
      </div>
    </div>

    <!-- Step 2 -->
    <div v-else-if="step === 2">
      <h1 class="text-2xl font-semibold text-slate-900">
        Your workflows will build themselves
      </h1>
      <p class="mt-1 text-slate-500">
        These are all available once we finish setting up.
      </p>
      <div class="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-3">
        <div
          v-for="m in CREATION_METHODS"
          :key="m.id"
          class="flex flex-col items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-5 text-center"
        >
          <component
            :is="m.icon"
            class="h-6 w-6 text-accent-600"
          />
          <span class="text-sm font-medium text-slate-700">{{ m.label }}</span>
        </div>
      </div>
      <div class="mt-8 flex justify-between">
        <FmButton
          variant="ghost"
          @click="onboarding.setStep(1)"
        >
          Back
        </FmButton>
        <FmButton @click="onboarding.setStep(3)">
          Continue
        </FmButton>
      </div>
    </div>

    <!-- Step 3 -->
    <div v-else>
      <h1 class="text-2xl font-semibold text-slate-900">
        Invite your first team member
      </h1>
      <p class="mt-1 text-slate-500">
        Optional — you can always invite people later from workspace settings.
      </p>
      <div class="mt-6 space-y-4">
        <FmInput
          v-model="inviteEmail"
          type="email"
          label="Email address"
          placeholder="teammate@company.com"
          help="The person must already have an account in your organisation."
        />
        <div>
          <label class="mb-1 block text-sm font-medium text-slate-700">Role</label>
          <select
            v-model="inviteRole"
            class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
          >
            <option
              v-for="r in inviteRoles"
              :key="r"
              :value="r"
            >
              {{ r }}
            </option>
          </select>
        </div>
      </div>
      <div class="mt-8 flex items-center justify-between">
        <FmButton
          variant="ghost"
          @click="skipInvite"
        >
          Skip for now
        </FmButton>
        <FmButton
          :loading="submitting"
          @click="finish"
        >
          Finish setup
        </FmButton>
      </div>
    </div>
  </div>
</template>
