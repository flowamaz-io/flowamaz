import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useAuthStore } from '@/stores/auth.store';

describe('auth.store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('logs in and hydrates the user + token', async () => {
    const store = useAuthStore();
    expect(store.isAuthenticated).toBe(false);

    await store.login({ email: 'jane@acme.com', password: 'correct', orgSlug: 'acme' });

    expect(store.accessToken).toBe('access-token-1');
    expect(store.user?.email).toBe('jane@acme.com');
    expect(store.isAuthenticated).toBe(true);
    // /me hydrates workspaces.
    expect(store.workspaces.length).toBeGreaterThan(0);
  });

  it('surfaces an actionable error on bad credentials', async () => {
    const store = useAuthStore();
    await expect(store.login({ email: 'jane@acme.com', password: 'wrong', orgSlug: 'acme' })).rejects.toBeTruthy();
    expect(store.isAuthenticated).toBe(false);
  });

  it('refreshes the access token', async () => {
    const store = useAuthStore();
    const ok = await store.refreshToken();
    expect(ok).toBe(true);
    expect(store.accessToken).toBe('refreshed-token');
  });

  it('clears state on logout', async () => {
    const store = useAuthStore();
    await store.login({ email: 'jane@acme.com', password: 'correct', orgSlug: 'acme' });
    expect(store.isAuthenticated).toBe(true);

    await store.logout();
    expect(store.accessToken).toBeNull();
    expect(store.user).toBeNull();
    expect(store.isAuthenticated).toBe(false);
  });
});
