import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router';
import { useAuthStore } from '@/stores/auth.store';
import { isOnboardingComplete } from '@/composables/useOnboarding';
import AppShell from '@/components/layout/AppShell.vue';

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/auth/LoginView.vue'),
    meta: { guestOnly: true },
  },
  {
    path: '/register',
    name: 'register',
    component: () => import('@/views/auth/RegisterView.vue'),
    meta: { guestOnly: true },
  },
  {
    path: '/onboarding',
    name: 'onboarding',
    component: () => import('@/views/onboarding/OnboardingView.vue'),
    meta: { requiresAuth: true },
  },
  {
    path: '/',
    component: AppShell,
    meta: { requiresAuth: true, requiresOnboarding: true },
    children: [
      { path: '', name: 'dashboard', component: () => import('@/views/dashboard/DashboardView.vue') },
      { path: 'workflows/new', name: 'workflow-new', component: () => import('@/views/workflow/NewWorkflowView.vue') },
      { path: 'workflows', name: 'workflows', component: () => import('@/views/workflow/WorkflowListView.vue') },
      { path: 'workflows/:id', name: 'workflow-detail', component: () => import('@/views/workflow/WorkflowDetailView.vue') },
      { path: 'workflows/:id/edit', name: 'workflow-editor', component: () => import('@/views/workflow/WorkflowEditorView.vue') },
      { path: 'instances', name: 'instances', component: () => import('@/views/instance/InstanceListView.vue') },
      { path: 'instances/:id', name: 'instance-detail', component: () => import('@/views/instance/InstanceDetailView.vue') },
      { path: 'settings', name: 'settings', component: () => import('@/views/workspace/WorkspaceSettingsView.vue') },
      { path: 'settings/members', name: 'members', component: () => import('@/views/workspace/MembersView.vue') },
      { path: 'settings/api-keys', name: 'api-keys', component: () => import('@/views/workspace/ApiKeysView.vue') },
    ],
  },
  { path: '/:pathMatch(.*)*', redirect: '/' },
];

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
});

router.beforeEach(async (to) => {
  const auth = useAuthStore();

  // Ensure we've attempted a silent refresh before deciding on auth-gated routes.
  if (!auth.initialised) {
    await auth.restoreSession();
  }

  // alreadyAuthenticated: keep logged-in users out of /login and /register.
  if (to.meta.guestOnly && auth.isAuthenticated) {
    return { path: '/' };
  }

  // requiresAuth.
  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { path: '/login', query: { redirect: to.fullPath } };
  }

  // requiresOnboarding: send new orgs through the wizard first.
  if (to.meta.requiresOnboarding && auth.isAuthenticated) {
    const orgId = auth.user?.orgId ?? '';
    if (!isOnboardingComplete(orgId)) {
      return { path: '/onboarding' };
    }
  }

  return true;
});
