import { describe, it, expect, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import RegisterView from '@/views/auth/RegisterView.vue';

// Run-6 finding 4: new organisations start on the free Community plan unless they pick another.

const register = vi.fn(async () => undefined);
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('@/composables/useAuth', () => ({ useAuth: () => ({ register }) }));

describe('RegisterView plan', () => {
  it('preselects Community and registers on it', async () => {
    setActivePinia(createPinia());
    const wrapper = mount(RegisterView, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } });

    const selected = wrapper.findAll('fieldset button').find((b) => b.classes().includes('ring-2'));
    expect(selected?.text()).toContain('Community');

    const inputs = wrapper.findAll('input');
    const byType = (t: string) => inputs.filter((i) => i.attributes('type') === t);
    const text = inputs.filter((i) => !['email', 'password'].includes(i.attributes('type') ?? ''));
    await text[0]!.setValue('Acme Lending');
    await text[1]!.setValue('acme-lending');
    await text[2]!.setValue('Ada Admin');
    await byType('email')[0]!.setValue('ada@acme.test');
    await byType('password')[0]!.setValue('Str0ng!Passw0rd');
    await byType('password')[1]!.setValue('Str0ng!Passw0rd');
    await wrapper.find('form').trigger('submit');
    await flushPromises();

    expect(register).toHaveBeenCalledWith(expect.objectContaining({ planSlug: 'community' }));
  });
});
