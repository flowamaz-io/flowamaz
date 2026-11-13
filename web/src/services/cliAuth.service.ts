import { http } from './api.service';

// `fmz auth login` device flow (FUNCTIONAL.md §CLI): the CLI shows a code, the signed-in browser approves it.
export const cliAuthService = {
  async approve(userCode: string): Promise<void> {
    await http.post('/api/v1/cli/auth/approve', { userCode });
  },
};

/** "abcd-1234" / " ABCD1234 " → "ABCD-1234"; the API matches codes exactly. */
export function normaliseUserCode(raw: string): string {
  const compact = raw.toUpperCase().replace(/[^A-Z0-9]/g, '');
  return compact.length === 8 ? `${compact.slice(0, 4)}-${compact.slice(4)}` : compact;
}
