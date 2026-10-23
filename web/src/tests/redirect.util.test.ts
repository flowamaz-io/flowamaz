import { describe, it, expect } from 'vitest';
import { postLoginTarget, safeRedirectPath } from '@/utils/redirect.util';

describe('safeRedirectPath', () => {
  it('accepts same-origin relative paths, keeping query and hash', () => {
    expect(safeRedirectPath('/workflows')).toBe('/workflows');
    expect(safeRedirectPath('/instances?status=failed#top')).toBe('/instances?status=failed#top');
    expect(safeRedirectPath(['/gates', '/other'])).toBe('/gates');
  });

  it('rejects external, protocol-relative and scheme URLs', () => {
    expect(safeRedirectPath('https://evil.example/phish')).toBeNull();
    expect(safeRedirectPath('//evil.example')).toBeNull();
    expect(safeRedirectPath('/\\evil.example')).toBeNull();
    expect(safeRedirectPath('javascript:alert(1)')).toBeNull();
    expect(safeRedirectPath('workflows')).toBeNull();
    expect(safeRedirectPath('/\t/evil.example')).toBeNull();
  });

  it('rejects empty, non-string and auth-page values', () => {
    expect(safeRedirectPath(undefined)).toBeNull();
    expect(safeRedirectPath(null)).toBeNull();
    expect(safeRedirectPath('')).toBeNull();
    expect(safeRedirectPath(42)).toBeNull();
    expect(safeRedirectPath('/login?redirect=/x')).toBeNull();
    expect(safeRedirectPath('/register')).toBeNull();
  });
});

describe('postLoginTarget', () => {
  it('honours a safe redirect once onboarding is complete', () => {
    expect(postLoginTarget('/workflows', true)).toBe('/workflows');
  });

  it('falls back to home for missing or unsafe redirects', () => {
    expect(postLoginTarget(undefined, true)).toBe('/');
    expect(postLoginTarget('https://evil.example', true)).toBe('/');
    expect(postLoginTarget('//evil.example', true)).toBe('/');
  });

  it('sends users to onboarding first, carrying the destination through', () => {
    expect(postLoginTarget('/workflows', false)).toBe('/onboarding?redirect=%2Fworkflows');
    expect(postLoginTarget(undefined, false)).toBe('/onboarding');
    expect(postLoginTarget('https://evil.example', false)).toBe('/onboarding');
  });

  it('never loops back into onboarding after it is complete', () => {
    expect(postLoginTarget('/onboarding', true)).toBe('/');
    expect(postLoginTarget('/onboarding', false)).toBe('/onboarding');
  });
});
