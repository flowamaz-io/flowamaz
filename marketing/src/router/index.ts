import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  { path: '/', name: 'home', component: () => import('@/views/HomeView.vue') },
  { path: '/pricing', name: 'pricing', component: () => import('@/views/PricingView.vue') },
  { path: '/about', name: 'about', component: () => import('@/views/AboutView.vue') },
  { path: '/blog', name: 'blog', component: () => import('@/views/BlogView.vue') },
  { path: '/changelog', name: 'changelog', component: () => import('@/views/ChangelogView.vue') },
];

export const router = createRouter({
  history: createWebHistory(),
  routes,
  // Always land at the top on navigation; honour the in-page anchor when present.
  scrollBehavior(to) {
    if (to.hash) return { el: to.hash, behavior: 'smooth' };
    return { top: 0 };
  },
});
