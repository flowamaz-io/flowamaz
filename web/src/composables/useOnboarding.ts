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

/** Reads onboarding completion for guards (no reactive ref needed). */
export function isOnboardingComplete(orgId: string): boolean {
  try {
    const raw = localStorage.getItem(ONBOARDING_KEY(orgId));
    if (!raw) return false;
    return Boolean((JSON.parse(raw) as Partial<OnboardingState>).completed);
  } catch {
    return false;
  }
}

/** Getting-started checklist dismissal (per org). */
export function isChecklistDismissed(orgId: string): boolean {
  return localStorage.getItem(CHECKLIST_DISMISS_KEY(orgId)) === 'true';
}

export function dismissChecklist(orgId: string): void {
  localStorage.setItem(CHECKLIST_DISMISS_KEY(orgId), 'true');
}
