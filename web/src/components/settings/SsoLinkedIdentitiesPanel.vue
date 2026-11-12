<script setup lang="ts">
import { onMounted, ref } from 'vue';
import dayjs from 'dayjs';
import { useDebounceFn } from '@vueuse/core';
import { Link2Off, UserRound } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { ssoService, type SsoLinkedIdentity } from '@/services/sso.service';

// Linked sign-ins: people whose Flowamaz account is tied to one identity at the IdP. Unlinking lets the next SSO
// sign-in link a new identity (e.g. after the person was deleted and re-created in the IdP).

const props = defineProps<{ orgId: string; currentUserId?: string }>();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const items = ref<SsoLinkedIdentity[]>([]);
const total = ref(0);
const search = ref('');
const confirming = ref<string | null>(null);
const unlinking = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    const page = await ssoService.getLinkedIdentities(props.orgId, search.value.trim() || undefined);
    items.value = page.items;
    total.value = page.total;
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);
const onSearch = useDebounceFn(load, 300);

async function unlink(person: SsoLinkedIdentity): Promise<void> {
  unlinking.value = person.orgUserId;
  try {
    await ssoService.unlinkIdentity(props.orgId, person.orgUserId);
    items.value = items.value.filter((p) => p.orgUserId !== person.orgUserId);
    total.value = Math.max(0, total.value - 1);
    confirming.value = null;
    toast.success(`Unlinked ${person.email}. Their next SSO sign-in links the identity your IdP sends then.`);
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    unlinking.value = null;
  }
}

const isSelf = (p: SsoLinkedIdentity) => p.orgUserId === props.currentUserId;
const lastSignIn = (iso: string | null) => (iso ? `Last signed in ${dayjs(iso).format('D MMM YYYY')}` : 'Not signed in yet');
</script>

<template>
  <section
    class="space-y-4 rounded-xl border border-slate-200 bg-white p-6"
    aria-labelledby="sso-identities-heading"
  >
    <div>
      <h2
        id="sso-identities-heading"
        class="text-sm font-semibold text-slate-900"
      >
        Linked sign-ins
      </h2>
      <p class="mt-1 text-xs text-slate-500">
        The first SSO sign-in ties a Flowamaz account to one identity at your identity provider, and from then on only
        that identity can sign in to it. If someone was deleted and re-created in your IdP, their sign-in is refused:
        unlink the old identity here and the next sign-in links the new one. Unlinking also ends their current sessions.
      </p>
    </div>

    <div class="max-w-sm">
      <FmInput
        v-model="search"
        label="Find a person"
        placeholder="Email or name"
        autocomplete="off"
        @update:model-value="onSearch"
      />
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-6 text-primary-600"
    >
      <FmSpinner />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load linked sign-ins"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <p
      v-else-if="items.length === 0"
      class="rounded-lg bg-slate-50 p-4 text-sm text-slate-600"
      data-test="sso-identities-empty"
    >
      <template v-if="search.trim()">
        Nobody with a linked sign-in matches "{{ search.trim() }}". Check the spelling, or search by part of the email.
      </template>
      <template v-else>
        No accounts are linked yet. Each person's account is linked the first time they sign in with SSO; they'll
        appear here then.
      </template>
    </p>

    <template v-else>
      <ul class="divide-y divide-slate-100 rounded-lg border border-slate-200">
        <li
          v-for="p in items"
          :key="p.orgUserId"
          class="flex flex-wrap items-center gap-3 px-3 py-2 text-sm"
          data-test="sso-identity"
        >
          <UserRound class="h-4 w-4 text-slate-400" />
          <span class="font-medium text-slate-800">{{ p.email }}</span>
          <span class="text-xs text-slate-500">{{ p.name }}<template v-if="p.isOrgOwner"> · Owner</template><template v-if="isSelf(p)"> · You</template></span>
          <span class="text-xs text-slate-500">{{ lastSignIn(p.lastLoginAt) }}</span>
          <FmButton
            v-if="confirming !== p.orgUserId"
            class="ml-auto"
            variant="ghost"
            :aria-label="`Unlink ${p.email}`"
            @click="confirming = p.orgUserId"
          >
            <template #icon-left>
              <Link2Off class="h-4 w-4" />
            </template>
            Unlink
          </FmButton>
          <div
            v-else
            class="ml-auto flex w-full flex-wrap items-center justify-end gap-2 sm:w-auto"
            role="group"
            :aria-label="`Confirm unlinking ${p.email}`"
          >
            <span class="text-xs text-red-700">
              {{ isSelf(p) ? 'Your next SSO sign-in links a new identity.' : 'They are signed out, and their next SSO sign-in links a new identity.' }}
            </span>
            <FmButton
              variant="danger"
              :loading="unlinking === p.orgUserId"
              @click="unlink(p)"
            >
              Unlink
            </FmButton>
            <FmButton
              variant="ghost"
              @click="confirming = null"
            >
              Keep
            </FmButton>
          </div>
        </li>
      </ul>
      <p
        v-if="total > items.length"
        class="text-xs text-slate-500"
      >
        Showing {{ items.length }} of {{ total }}. Search to find someone else.
      </p>
    </template>
  </section>
</template>
