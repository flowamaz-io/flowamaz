/**
 * Validates a post-login `?redirect=` value so it can only send the user to a
 * same-origin, in-app path. Anything else (absolute URLs, protocol-relative
 * `//host`, backslash tricks, `javascript:` etc.) is rejected to avoid open redirects.
 *
 * Returns the safe path, or `null` when the value must be ignored.
 */
export function safeRedirectPath(raw: unknown): string | null {
  const value = Array.isArray(raw) ? raw[0] : raw;
  if (typeof value !== 'string') return null;
  const path = value.trim();
  if (!path.startsWith('/')) return null;
  // Protocol-relative ("//evil.com") and the browser-normalised backslash variant ("/\evil.com").
  if (path.startsWith('//') || path.startsWith('/\\')) return null;
  // Control characters (tabs/newlines are stripped by URL parsers and can smuggle a host).
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f]/.test(path)) return null;
  // Never bounce back into the auth screens themselves.
  if (/^\/(login|register)(?:[/?#]|$)/.test(path)) return null;
  return path;
}

const isOnboardingPath = (path: string): boolean => /^\/onboarding(?:[/?#]|$)/.test(path);

/**
 * Where to send a user straight after signing in (a router-pushable full path).
 * Onboarding always comes first; the original destination is carried through it.
 */
export function postLoginTarget(rawRedirect: unknown, onboardingComplete: boolean): string {
  const redirect = safeRedirectPath(rawRedirect);
  const destination = redirect && !isOnboardingPath(redirect) ? redirect : null;
  if (!onboardingComplete) {
    return destination ? `/onboarding?redirect=${encodeURIComponent(destination)}` : '/onboarding';
  }
  return destination ?? '/';
}
