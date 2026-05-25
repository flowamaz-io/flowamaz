import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { authService } from '@/services/auth.service';
import { registerAuthBridge } from '@/services/api.service';
import type { LoginRequest, MeResponse, RegisterRequest, UserSummary, WorkspaceSummary } from '@/types';

/**
 * Auth store. The access token lives in memory ONLY (never localStorage) to limit XSS blast
 * radius — the refresh token is an httpOnly cookie the browser sends automatically. On app
 * init we attempt a silent refresh to restore the session for a returning user.
 */
export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(null);
  const user = ref<UserSummary | null>(null);
  const workspaces = ref<WorkspaceSummary[]>([]);
  const initialised = ref(false);

  const isAuthenticated = computed(() => accessToken.value !== null && user.value !== null);

  function setToken(token: string): void {
    accessToken.value = token;
  }

  function clear(): void {
    accessToken.value = null;
    user.value = null;
    workspaces.value = [];
  }

  function applyMe(me: MeResponse): void {
    user.value = { id: me.id, email: me.email, name: me.name, orgId: me.orgId, orgSlug: me.orgSlug };
    workspaces.value = me.workspaces;
  }

  async function login(payload: LoginRequest): Promise<void> {
    const res = await authService.login(payload);
    accessToken.value = res.accessToken;
    user.value = res.user;
    await loadProfile();
  }

  async function register(payload: RegisterRequest): Promise<void> {
    const res = await authService.register(payload);
    accessToken.value = res.accessToken;
    user.value = res.user;
    await loadProfile();
  }

  async function loadProfile(): Promise<void> {
    const me = await authService.me();
    applyMe(me);
  }

  async function refreshToken(): Promise<boolean> {
    try {
      const res = await authService.refresh();
      accessToken.value = res.accessToken;
      return true;
    } catch {
      clear();
      return false;
    }
  }

  /** Silent session restore on app boot — refresh, then hydrate profile. */
  async function restoreSession(): Promise<void> {
    if (initialised.value) return;
    const ok = await refreshToken();
    if (ok) {
      try {
        await loadProfile();
      } catch {
        clear();
      }
    }
    initialised.value = true;
  }

  async function logout(): Promise<void> {
    try {
      await authService.logout();
    } finally {
      clear();
    }
  }

  // Wire the Axios bridge once, so the interceptor can read/refresh the token without a
  // circular import. onAuthFailure clears local state; the router guard handles redirect.
  registerAuthBridge({
    getToken: () => accessToken.value,
    setToken,
    onAuthFailure: () => {
      clear();
    },
  });

  return {
    accessToken,
    user,
    workspaces,
    initialised,
    isAuthenticated,
    login,
    register,
    loadProfile,
    refreshToken,
    restoreSession,
    logout,
    clear,
    setToken,
  };
});
