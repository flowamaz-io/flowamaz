<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { Users, UserPlus } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmModal from '@/components/common/FmModal.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { useAuth } from '@/composables/useAuth';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { formatDate } from '@/utils/date.util';
import { initial } from '@/utils/string.util';
import type { MemberResponse, WorkspaceRole } from '@/types';

const ws = useWorkspace();
const { user } = useAuth();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');

const inviteOpen = ref(false);
const inviteEmail = ref('');
const inviteRole = ref<WorkspaceRole>('Designer');
const inviting = ref(false);

const removeTarget = ref<MemberResponse | null>(null);
const removing = ref(false);

const roleOptions: WorkspaceRole[] = ['Designer', 'Operator', 'Runner', 'Viewer'];
const changeRoleOptions: WorkspaceRole[] = ['Admin', 'Designer', 'Operator', 'Runner', 'Viewer'];

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    await ws.loadMembers();
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);
watch(() => ws.currentWorkspaceId.value, load);

async function submitInvite(): Promise<void> {
  if (!inviteEmail.value.trim()) {
    toast.error('Enter the email address of an existing org member to invite them.');
    return;
  }
  inviting.value = true;
  try {
    await ws.inviteMember({ email: inviteEmail.value.trim(), role: inviteRole.value });
    toast.success(`${inviteEmail.value.trim()} added to the workspace.`);
    inviteOpen.value = false;
    inviteEmail.value = '';
    inviteRole.value = 'Designer';
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    inviting.value = false;
  }
}

async function changeRole(member: MemberResponse, role: WorkspaceRole): Promise<void> {
  try {
    await ws.updateMemberRole(member.orgUserId, role);
    toast.success(`${member.name}'s role updated to ${role}.`);
  } catch (e) {
    toast.error(toUserFacingError(e).message);
    await load();
  }
}

async function confirmRemove(): Promise<void> {
  if (!removeTarget.value) return;
  removing.value = true;
  try {
    await ws.removeMember(removeTarget.value.orgUserId);
    toast.success(`${removeTarget.value.name} removed from the workspace.`);
    removeTarget.value = null;
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    removing.value = false;
  }
}

function isSelf(member: MemberResponse): boolean {
  return member.orgUserId === user.value?.id;
}
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6 p-6">
    <header class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-semibold text-slate-900">
          Members
        </h1>
        <p class="text-sm text-slate-500">
          Manage who can access this workspace and what they can do.
        </p>
      </div>
      <FmButton @click="inviteOpen = true">
        <template #icon-left>
          <UserPlus class="h-4 w-4" />
        </template>
        Invite member
      </FmButton>
    </header>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load members"
      :description="loadError"
      action-label="Try again"
      help-article="workspaces/invite-team-members"
      @action="load"
    />

    <FmEmptyState
      v-else-if="ws.members.value.length === 0"
      :icon="Users"
      title="No members yet"
      description="Invite your teammates so they can design, run, and monitor workflows together."
      cta-label="Invite your first team member"
      @cta="inviteOpen = true"
    />

    <div
      v-else
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <thead class="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">
              Member
            </th>
            <th class="px-4 py-3">
              Role
            </th>
            <th class="px-4 py-3">
              Joined
            </th>
            <th class="px-4 py-3 text-right">
              Actions
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="m in ws.members.value"
            :key="m.orgUserId"
          >
            <td class="px-4 py-3">
              <div class="flex items-center gap-3">
                <span class="flex h-8 w-8 items-center justify-center rounded-full bg-primary-100 text-sm font-semibold text-primary-700">
                  {{ initial(m.name) }}
                </span>
                <div>
                  <p class="font-medium text-slate-800">
                    {{ m.name }}
                  </p>
                  <p class="text-xs text-slate-400">
                    {{ m.email }}
                  </p>
                </div>
              </div>
            </td>
            <td class="px-4 py-3">
              <select
                v-if="!isSelf(m)"
                :value="m.role"
                class="rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm focus:border-primary-500 focus:outline-none"
                @change="changeRole(m, ($event.target as HTMLSelectElement).value as WorkspaceRole)"
              >
                <option
                  v-for="r in changeRoleOptions"
                  :key="r"
                  :value="r"
                >
                  {{ r }}
                </option>
              </select>
              <FmBadge
                v-else
                :role="m.role"
              />
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ formatDate(m.joinedAt) }}
            </td>
            <td class="px-4 py-3 text-right">
              <button
                v-if="!isSelf(m)"
                type="button"
                class="text-sm font-medium text-danger-600 hover:text-danger-700"
                @click="removeTarget = m"
              >
                Remove
              </button>
              <span
                v-else
                class="text-xs text-slate-400"
              >You</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Invite modal -->
    <FmModal
      v-model="inviteOpen"
      title="Invite a team member"
      size="sm"
    >
      <div class="space-y-4">
        <FmInput
          v-model="inviteEmail"
          type="email"
          label="Email"
          placeholder="teammate@company.com"
          help="The person must already belong to your organisation."
          help-article="workspaces/invite-team-members"
        />
        <div>
          <label class="mb-1 block text-sm font-medium text-slate-700">Role</label>
          <select
            v-model="inviteRole"
            class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
          >
            <option
              v-for="r in roleOptions"
              :key="r"
              :value="r"
            >
              {{ r }}
            </option>
          </select>
        </div>
      </div>
      <template #footer>
        <FmButton
          variant="secondary"
          @click="inviteOpen = false"
        >
          Cancel
        </FmButton>
        <FmButton
          :loading="inviting"
          @click="submitInvite"
        >
          Send invite
        </FmButton>
      </template>
    </FmModal>

    <!-- Remove confirmation -->
    <FmModal
      :model-value="removeTarget !== null"
      title="Remove member"
      size="sm"
      @update:model-value="removeTarget = null"
    >
      <p class="text-sm text-slate-600">
        Remove <strong>{{ removeTarget?.name }}</strong> from this workspace? They will lose access immediately. You can
        re-invite them at any time.
      </p>
      <template #footer>
        <FmButton
          variant="secondary"
          @click="removeTarget = null"
        >
          Cancel
        </FmButton>
        <FmButton
          variant="danger"
          :loading="removing"
          @click="confirmRemove"
        >
          Remove
        </FmButton>
      </template>
    </FmModal>
  </div>
</template>
