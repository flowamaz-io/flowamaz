import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { ref } from 'vue';
import PlatformApiLimitsView from '@/views/platform/PlatformApiLimitsView.vue';
import type { PlatformOrgApiLimit } from '@/services/platform.service';

// Staff screen for agreed API limits: closed to non-staff, list, change, back to the plan limit, input checks.

const user = ref<{ isPlatformStaff?: boolean } | null>({ isPlatformStaff: true });
vi.mock('@/composables/useAuth', () => ({ useAuth: () => ({ user }) }));
let orgs: PlatformOrgApiLimit[];
const search = vi.fn(async (_s?: string) => orgs);
const setLimit = vi.fn(async (orgId: string, perMinute: number | null) => {
  const o = orgs.find((x) => x.orgId === orgId)!;
  return { ...o, customPerMinute: perMinute, effectivePerMinute: perMinute ?? o.planPerMinute };
});
vi.mock('@/services/platform.service', () => ({
  platformService: {
    searchOrganisations: (s?: string) => search(s),
    setApiLimit: (id: string, p: number | null) => setLimit(id, p),
  },
}));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('@/utils/error.util', () => ({ toUserFacingError: () => ({ message: 'Unavailable. Try again.' }) }));

const acme: PlatformOrgApiLimit = {
  orgId: 'o1', name: 'Acme', slug: 'acme', planName: 'Enterprise', planPerMinute: 1200, customPerMinute: null, effectivePerMinute: 1200,
};

async function mountView() {
  const wrapper = mount(PlatformApiLimitsView);
  await flushPromises();
  return wrapper;
}

describe('PlatformApiLimitsView', () => {
  beforeEach(() => {
    user.value = { isPlatformStaff: true };
    orgs = [acme];
    search.mockClear();
    setLimit.mockClear();
  });

  it('explains the page to people who are not staff and loads nothing', async () => {
    user.value = { isPlatformStaff: false };
    const wrapper = await mountView();
    expect(wrapper.text()).toContain('This page is for Flowamaz staff');
    expect(search).not.toHaveBeenCalled();
  });

  it('sets an agreed limit', async () => {
    const wrapper = await mountView();
    expect(wrapper.text()).toContain('Uses the plan limit');
    await wrapper.find('[aria-label="Change the API limit of Acme"]').trigger('click');
    await wrapper.find('form input').setValue('5,000');
    await wrapper.find('form').trigger('submit');
    await flushPromises();
    expect(setLimit).toHaveBeenCalledWith('o1', 5000);
    expect(wrapper.text()).toContain('Agreed: 5,000 calls a minute');
  });

  it('refuses a limit that is not a whole number in range', async () => {
    const wrapper = await mountView();
    await wrapper.find('[aria-label="Change the API limit of Acme"]').trigger('click');
    await wrapper.find('form input').setValue('lots');
    await wrapper.find('form').trigger('submit');
    await flushPromises();
    expect(setLimit).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('Enter a whole number from 0 to 100,000');
  });

  it('teaches what to do when the search finds nothing', async () => {
    orgs = [];
    const wrapper = await mountView();
    expect(wrapper.find('[data-test="platform-orgs-empty"]').exists()).toBe(true);
  });
});
