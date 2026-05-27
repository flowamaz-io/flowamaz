import type { Component } from 'vue';
import { Type, Mic, Camera, MessagesSquare, FileText, MousePointer2 } from 'lucide-vue-next';
import type { WorkspaceRole } from '@/types';

export const APP_NAME = import.meta.env.VITE_APP_NAME || 'Flowamaz';
export const HELP_DOCS_URL = import.meta.env.VITE_HELP_DOCS_URL || 'https://docs.flowamaz.io';
export const DOCS_GITHUB_BASE = 'https://github.com/flowamaz-io/docs/blob/main/docs';

/** Local-storage key prefixes (per-org so multiple orgs on one device stay isolated). */
export const ONBOARDING_KEY = (orgId: string): string => `fmz_onboarding_${orgId}`;
export const CHECKLIST_DISMISS_KEY = (orgId: string): string => `fmz_checklist_dismissed_${orgId}`;

/** The seven platform AI functions (FUNCTIONAL.md §5) keyed by backend functionId. */
export const AI_FUNCTIONS: { id: string; label: string; description: string }[] = [
  { id: 'copilot', label: 'F1 · Co-pilot', description: 'In-canvas assistant' },
  { id: 'nl-yaml', label: 'F2 · NL → YAML', description: 'Plain-English to workflow' },
  { id: 'visual-input', label: 'F3 · Visual input', description: 'Whiteboard / image parsing' },
  { id: 'node-exec', label: 'F4 · Node execution', description: 'AI nodes inside workflows' },
  { id: 'process-intel', label: 'F5 · Process intelligence', description: 'Batch analytics' },
  { id: 'help-assist', label: 'F6 · Help assistant', description: 'In-app help Q&A' },
  { id: 'doc-parse', label: 'F7 · Document parsing', description: 'SOP / document extraction' },
];

/** Provider → model options offered in the AI config selector. */
export const PROVIDER_MODELS: Record<string, string[]> = {
  anthropic: ['claude-haiku-4-5', 'claude-sonnet-4-6', 'claude-opus-4-6'],
  'azure-openai': ['gpt-4o', 'gpt-4o-mini'],
  google: ['gemini-2.5-flash', 'gemini-2.5-pro'],
  kimi: ['kimi-k2'],
  mistral: ['mistral-large', 'mistral-small'],
  byom: ['custom'],
};

/** Friendly labels for AI providers. */
export const PROVIDER_LABELS: Record<string, string> = {
  anthropic: 'Anthropic',
  'azure-openai': 'Azure OpenAI',
  google: 'Google',
  kimi: 'Kimi',
  mistral: 'Mistral',
  byom: 'Bring your own model',
};

/** Key source options for AI config overrides. */
export const KEY_SOURCES: { value: string; label: string }[] = [
  { value: 'Platform', label: 'Platform key' },
  { value: 'Byok', label: 'Bring your own key' },
];

/** Run-retention options shown in workspace settings (days). */
export const RETENTION_OPTIONS = [7, 30, 90, 180, 365];

/** Marketplace policy options. */
export const MARKETPLACE_POLICIES: { value: string; label: string }[] = [
  { value: 'OfficialAndVerified', label: 'Official & verified only' },
  { value: 'AllowAll', label: 'Allow all' },
  { value: 'Allowlist', label: 'Allowlist only' },
];

/** API key scopes (FUNCTIONAL.md §4.6). */
export const API_KEY_SCOPES: { value: string; label: string }[] = [
  { value: 'workflows:read', label: 'Read workflows' },
  { value: 'workflows:trigger', label: 'Trigger workflows' },
  { value: 'instances:read', label: 'Read instances' },
  { value: 'instances:write', label: 'Write instances' },
  { value: 'webhooks:manage', label: 'Manage webhooks' },
  { value: 'instances:variables:read', label: 'Read instance variables' },
];

/** Registration plans (FUNCTIONAL.md §8) shown as cards. */
export const PLANS: { slug: string; name: string; price: string; blurb: string }[] = [
  { slug: 'community', name: 'Community', price: 'Free', blurb: 'Self-host, unlimited local workflows.' },
  { slug: 'starter', name: 'Starter', price: '$49/mo', blurb: 'Hosted, 3 workspaces, email support.' },
  { slug: 'pro', name: 'Pro', price: '$199/mo', blurb: 'Unlimited workspaces, SSO, priority support.' },
];

/** The six workflow creation methods. method is the ?method= query param for /workflows/new. */
export const CREATION_METHODS: { id: string; label: string; icon: Component; method: string }[] = [
  { id: 'nl', label: 'Plain English', icon: Type, method: 'nl' },
  { id: 'voice', label: 'Voice', icon: Mic, method: 'voice' },
  { id: 'visual', label: 'Whiteboard photo', icon: Camera, method: 'visual' },
  { id: 'conversation', label: 'Slack conversation', icon: MessagesSquare, method: 'conversation' },
  { id: 'document', label: 'Upload a document', icon: FileText, method: 'document' },
  { id: 'canvas', label: 'Drag & drop', icon: MousePointer2, method: 'canvas' },
];

/** Role badge colour classes (Tailwind). */
export const ROLE_BADGE: Record<WorkspaceRole, string> = {
  Admin: 'bg-accent-100 text-accent-700',
  Designer: 'bg-primary-100 text-primary-700',
  Operator: 'bg-amber-100 text-amber-700',
  Runner: 'bg-blue-100 text-blue-700',
  Viewer: 'bg-slate-100 text-slate-600',
};
