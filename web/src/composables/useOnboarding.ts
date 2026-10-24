import { computed, ref } from 'vue';
import { ONBOARDING_KEY, CHECKLIST_DISMISS_KEY } from '@/utils/constants';

export interface OnboardingState {
  step: number;
  completed: boolean;
  workspaceCreated: boolean;
  memberInvited: boolean;
}

const DEFAULT: OnboardingState = {
  step: 1,
  completed: false,
  workspaceCreated: false,
  memberInvited: false,
};

/** Persists the onboarding wizard's progress per org so a refresh resumes the last step. */
export function useOnboarding(orgId: string) {
  const key = ONBOARDING_KEY(orgId);
  const state = ref<OnboardingState>(load());

  function load(): OnboardingState {
    try {
      const raw = localStorage.getItem(key);
      return raw ? { ...DEFAULT, ...(JSON.parse(raw) as Partial<OnboardingState>) } : { ...DEFAULT };
    } catch {
      return { ...DEFAULT };
    }
  }

  function persist(): void {
    localStorage.setItem(key, JSON.stringify(state.value));
  }

  function setStep(step: number): void {
    state.value.step = step;
    persist();
  }

  function markWorkspaceCreated(): void {
    state.value.workspaceCreated = true;
    persist();
  }

  function markMemberInvited(): void {
    state.value.memberInvited = true;
    persist();
  }

  function complete(): void {
    state.value.completed = true;
    persist();
  }

  return {
    state,
    isComplete: computed(() => state.value.completed),
    setStep,
    markWorkspaceCreated,
    markMemberInvited,
    complete,
  };
}

/**
 * Whether this user still needs the onboarding wizard (create the first workspace, invite a teammate).
 *
 * Wizard progress lives in this browser's localStorage, but the org may already be set up without it: a workspace
 * created through the API, by another admin, or in another browser, or a member who was invited into one. So a
 * user who already belongs to a workspace is past onboarding — unless they are part-way through the wizard in this
 * browser, which then resumes where they left off.
 */
export function isOnboardingComplete(orgId: string, workspaceCount = 0): boolean {
  let saved: Partial<OnboardingState> | null = null;
  try {
    const raw = localStorage.getItem(ONBOARDING_KEY(orgId));
    saved = raw ? (JSON.parse(raw) as Partial<OnboardingState>) : null;
  } catch {
    saved = null;
  }
  if (saved?.completed) return true;
  const wizardInProgress = saved !== null && (saved.step ?? 1) > 1;
  return workspaceCount > 0 && !wizardInProgress;
}

/** Getting-started checklist dismissal (per org). */
export function isChecklistDismissed(orgId: string): boolean {
  return localStorage.getItem(CHECKLIST_DISMISS_KEY(orgId)) === 'true';
}

export function dismissChecklist(orgId: string): void {
  localStorage.setItem(CHECKLIST_DISMISS_KEY(orgId), 'true');
}
