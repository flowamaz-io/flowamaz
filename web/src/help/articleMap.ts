/** Maps a route path to its default help article slug (article filename without `.md`). */
export const articleMap: Record<string, string> = {
  '/login': 'getting-started/cloud-signup',
  '/register': 'getting-started/cloud-signup',
  '/onboarding': 'getting-started/what-is-flowamaz',
  '/': 'getting-started/what-is-flowamaz',
  '/settings': 'workspaces/what-is-a-workspace',
  '/settings/members': 'workspaces/invite-team-members',
  '/settings/api-keys': 'workspaces/api-keys',
  // Phase 3+ routes added in later prompts.
};

/** Falls back to the platform overview when a route has no explicit mapping. */
export function articleForRoute(path: string): string {
  return articleMap[path] ?? 'getting-started/what-is-flowamaz';
}
