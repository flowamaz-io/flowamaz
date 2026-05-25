import { storeToRefs } from 'pinia';
import { useAuthStore } from '@/stores/auth.store';

/** Ergonomic accessor for auth state + actions in components. */
export function useAuth() {
  const store = useAuthStore();
  const { user, workspaces, isAuthenticated, accessToken } = storeToRefs(store);
  return {
    user,
    workspaces,
    isAuthenticated,
    accessToken,
    login: store.login,
    register: store.register,
    logout: store.logout,
    restoreSession: store.restoreSession,
  };
}
