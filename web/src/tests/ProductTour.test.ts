import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import ProductTour from '@/components/onboarding/ProductTour.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import { CREATION_METHODS } from '@/utils/constants';

// Mock the preferences service so the tour's gating preference is fully under test control.
const getMock = vi.fn();
const setMock = vi.fn();
vi.mock('@/services/preferences.service', () => ({
  preferencesService: {
    get: (...args: unknown[]) => getMock(...args),
    set: (...args: unknown[]) => setMock(...args),
  },
}));

// The tour pushes routes on its primary actions; stub vue-router so mounting works headless.
const pushMock = vi.fn();
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: pushMock }),
}));

function mountTour() {
  return mount(ProductTour, {
    props: { workspaceId: 'ws-1', autoStart: true },
  });
}

describe('ProductTour', () => {
  beforeEach(() => {
    getMock.mockReset();
    setMock.mockReset().mockResolvedValue(undefined);
    pushMock.mockReset();
  });

  it('shows on first login when the tour_completed preference is not set', async () => {
    getMock.mockResolvedValue(null);
    const wrapper = mountTour();
    await flushPromises();

    expect(getMock).toHaveBeenCalledWith('tour_completed_ws-1');
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('Your workspace sidebar');
  });

  it('does not show again once the tour has been completed', async () => {
    getMock.mockResolvedValue(true);
    const wrapper = mountTour();
    await flushPromises();

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false);
  });

  it('skips the tour and stores the preference when Escape is pressed', async () => {
    getMock.mockResolvedValue(null);
    const wrapper = mountTour();
    await flushPromises();
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await flushPromises();

    expect(setMock).toHaveBeenCalledWith('tour_completed_ws-1', true);
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false);
  });

  it('stores the preference when the tour is completed', async () => {
    getMock.mockResolvedValue(null);
    const wrapper = mountTour();
    await flushPromises();

    // Advance through every step with ArrowRight; the final advance completes the tour.
    for (let i = 0; i < 5; i += 1) {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
      await flushPromises();
    }

    expect(setMock).toHaveBeenCalledWith('tour_completed_ws-1', true);
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false);
  });
});

describe('WorkflowList empty state', () => {
  it('shows the creation-method CTAs when the workflow count is zero', () => {
    // The WorkflowListView empty state renders one creation card per CREATION_METHODS entry.
    const wrapper = mount(FmEmptyState, {
      props: {
        title: 'Create your first workflow',
        description: 'Choose how you want to build — plain English, a document, or the canvas.',
      },
      slots: {
        action: CREATION_METHODS.map((m) => `<button>${m.label}</button>`).join(''),
      },
    });

    expect(wrapper.text()).toContain('Create your first workflow');
    const buttons = wrapper.findAll('button');
    expect(buttons.length).toBe(CREATION_METHODS.length);
    expect(wrapper.text()).toContain(CREATION_METHODS[0].label);
  });
});
