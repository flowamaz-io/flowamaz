import { beforeEach, describe, expect, it } from 'vitest';
import { isOnboardingComplete } from '@/composables/useOnboarding';
import { ONBOARDING_KEY } from '@/utils/constants';

// A user who already belongs to a workspace (created through the API, by another admin, in another browser,
// or as an invited member) is past onboarding; this browser's wizard progress still wins when it exists.

const org = 'org-1';
const save = (state: object) => localStorage.setItem(ONBOARDING_KEY(org), JSON.stringify(state));

describe('isOnboardingComplete', () => {
  beforeEach(() => localStorage.clear());

  it('sends a new org with no workspace through the wizard', () => {
    expect(isOnboardingComplete(org, 0)).toBe(false);
  });

  it('skips the wizard for a user who already has a workspace and never used it here', () => {
    expect(isOnboardingComplete(org, 1)).toBe(true);
  });

  it('respects a wizard finished in this browser', () => {
    save({ completed: true, step: 3 });
    expect(isOnboardingComplete(org, 0)).toBe(true);
  });

  it('resumes a wizard left part-way in this browser, even after its workspace was created', () => {
    save({ completed: false, step: 2, workspaceCreated: true });
    expect(isOnboardingComplete(org, 1)).toBe(false);
  });

  it('ignores unreadable saved state', () => {
    localStorage.setItem(ONBOARDING_KEY(org), '{not json');
    expect(isOnboardingComplete(org, 1)).toBe(true);
    expect(isOnboardingComplete(org, 0)).toBe(false);
  });
});
