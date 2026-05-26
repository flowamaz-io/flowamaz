/** Maps a route path to its default help article slug (article filename without `.md`). */
export const articleMap: Record<string, string> = {
  '/login': 'getting-started/cloud-signup',
  '/register': 'getting-started/cloud-signup',
  '/onboarding': 'getting-started/what-is-flowamaz',
  '/': 'getting-started/what-is-flowamaz',
  '/settings': 'workspaces/what-is-a-workspace',
  '/settings/members': 'workspaces/invite-team-members',
  '/settings/api-keys': 'workspaces/api-keys',
  // Phase 3 editor routes (prompt 03-06)
  '/workflows/:id/edit': 'node-types/using-the-canvas',
  '/workflows/:id/edit#node:trigger': 'node-types/trigger-nodes',
  '/workflows/:id/edit#node:action': 'node-types/action-nodes',
  '/workflows/:id/edit#node:ai': 'node-types/ai-nodes',
  '/workflows/:id/edit#node:human-gate': 'node-types/human-gate-nodes',
  '/workflows/:id/edit#node:router': 'node-types/router-nodes',
  '/workflows/new': 'getting-started/what-is-flowamaz',
};

/** Falls back to the platform overview when a route has no explicit mapping. */
export function articleForRoute(path: string): string {
  return articleMap[path] ?? 'getting-started/what-is-flowamaz';
}

/** Maps an HTTP status code to the help article that best explains it (FmErrorState). */
export const errorArticleMap: Record<number, string> = {
  401: 'getting-started/cloud-signup',
  403: 'workspaces/roles-and-permissions',
  404: 'getting-started/key-concepts-glossary',
  429: 'billing/community-vs-cloud',
};

/** Maps an API error `code` to the help article that best explains it. */
export const errorCodeArticleMap: Record<string, string> = {
  workspace_not_found: 'workspaces/what-is-a-workspace',
  api_key_invalid: 'workspaces/api-keys',
  plan_limit_exceeded: 'billing/upgrade-guide',
};

/**
 * Resolves the help article for an error. A specific API error `code` wins over the
 * generic HTTP `statusCode`. Returns `null` when neither maps to an article.
 */
export function articleForError(statusCode?: number, code?: string): string | null {
  if (code && errorCodeArticleMap[code]) return errorCodeArticleMap[code];
  if (statusCode !== undefined && errorArticleMap[statusCode]) return errorArticleMap[statusCode];
  return null;
}
