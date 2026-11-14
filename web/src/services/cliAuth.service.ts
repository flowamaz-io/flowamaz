import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

/** A signed-in `fmz` CLI, as listed in Settings → CLI sessions. */
export interface CliSession {
  id: string;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
  lastUsedIp: string | null;
  expiresAt: string;
  /** True when the list was requested by this very CLI session. */
  isCurrent: boolean;
}

// `fmz auth login` device flow (FUNCTIONAL.md §15): the CLI shows a code, the signed-in browser approves it, and the
// CLI gets a session of its own. Settings → CLI sessions lists those sessions and ends them.
export const cliAuthService = {
  async approve(userCode: string): Promise<void> {
    await http.post('/api/v1/cli/auth/approve', { userCode });
  },

  /** The name the CLI gave itself for a pending login (e.g. "fmz on jagan-mbp"); null when the code isn't pending. */
  async pendingClientName(userCode: string): Promise<string | null> {
    try {
      const res = await http.get<ApiEnvelope<{ clientName: string }>>('/api/v1/cli/auth/pending', { params: { userCode } });
      return unwrap(res).clientName ?? null;
    } catch {
      return null;
    }
  },

  async listSessions(): Promise<CliSession[]> {
    const res = await http.get<ApiEnvelope<CliSession[]>>('/api/v1/cli/sessions');
    return unwrap(res) ?? [];
  },

  async revokeSession(id: string): Promise<void> {
    await http.delete(`/api/v1/cli/sessions/${id}`);
  },
};

/** "abcd-1234" / " ABCD1234 " → "ABCD-1234"; the API matches codes exactly. */
export function normaliseUserCode(raw: string): string {
  const compact = raw.toUpperCase().replace(/[^A-Z0-9]/g, '');
  return compact.length === 8 ? `${compact.slice(0, 4)}-${compact.slice(4)}` : compact;
}
