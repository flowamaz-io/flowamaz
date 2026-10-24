import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ref } from 'vue';
import ApiKeysView from '@/views/workspace/ApiKeysView.vue';
import type { ApiKeyResponse } from '@/types';

// B14: the "copy your new key" banner must show for every key created on the page,
// including after an earlier banner was dismissed (no reload).

const apiKeys = ref<ApiKeyResponse[]>([]);
const environments = ref([{ id: 'env-dev', name: 'Dev' }]);
const currentWorkspaceId = ref('ws-1');
const createApiKey = vi.fn();

vi.mock('@/composables/useWorkspace', () => ({
  useWorkspace: () => ({
    apiKeys,
    environments,
    currentWorkspaceId,
    loadApiKeys: vi.fn().mockResolvedValue(undefined),
    loadEnvironments: vi.fn().mockResolvedValue(undefined),
    createApiKey,
    revokeApiKey: vi.fn(),
  }),
}));

function keyRow(id: string, name: string): ApiKeyResponse {
  return {
    id,
    name,
    keyPrefix: `fmz_test_${id}`,
    environmentId: 'env-dev',
    scopes: ['workflows:read'],
    lastUsedAt: null,
    isActive: true,
  } as unknown as ApiKeyResponse;
}

function mountView(): VueWrapper {
  return mount(ApiKeysView, { global: { stubs: { teleport: true } }, attachTo: document.body });
}

async function createKey(wrapper: VueWrapper, name: string): Promise<void> {
  const openBtn = wrapper.findAll('button').find((b) => b.text() === 'Create key');
  await openBtn!.trigger('click');
  await flushPromises();
  await wrapper.find('input[placeholder="e.g. CI pipeline"]').setValue(name);
  // The modal's submit button is the last "Create key" button in the DOM.
  const submit = wrapper.findAll('button').filter((b) => b.text() === 'Create key').at(-1);
  await submit!.trigger('click');
  await flushPromises();
}

describe('ApiKeysView new-key banner', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    apiKeys.value = [];
    createApiKey.mockReset();
    let n = 0;
    createApiKey.mockImplementation(async ({ name }: { name: string }) => {
      n += 1;
      apiKeys.value = [...apiKeys.value, keyRow(`k${n}`, name)];
      return { ...keyRow(`k${n}`, name), plainKey: `fmz_test_secret_${n}` };
    });
  });

  it('shows the banner for a second key after the first banner was dismissed', async () => {
    const wrapper = mountView();
    await flushPromises();

    await createKey(wrapper, 'First');
    expect(wrapper.find('[data-testid="new-api-key-value"]').text()).toBe('fmz_test_secret_1');

    await wrapper.find('[data-testid="new-api-key-banner"] button[aria-label="Dismiss"]').trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="new-api-key-banner"]').exists()).toBe(false);

    await createKey(wrapper, 'Second');
    expect(createApiKey).toHaveBeenCalledTimes(2);
    expect(wrapper.find('[data-testid="new-api-key-banner"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="new-api-key-value"]').text()).toBe('fmz_test_secret_2');
    wrapper.unmount();
  });

  it('replaces the banner value when a second key is created without dismissing', async () => {
    const wrapper = mountView();
    await flushPromises();

    await createKey(wrapper, 'First');
    await createKey(wrapper, 'Second');
    expect(wrapper.findAll('[data-testid="new-api-key-banner"]')).toHaveLength(1);
    expect(wrapper.find('[data-testid="new-api-key-value"]').text()).toBe('fmz_test_secret_2');
    wrapper.unmount();
  });

  it('explains inline that a scope is required when none is selected', async () => {
    const wrapper = mountView();
    await flushPromises();

    const openBtn = wrapper.findAll('button').find((b) => b.text() === 'Create key');
    await openBtn!.trigger('click');
    await flushPromises();
    await wrapper.find('input[placeholder="e.g. CI pipeline"]').setValue('No scopes');
    // Untick the default "Read workflows" scope.
    await wrapper.find('input[type="checkbox"]:checked').trigger('change');
    const submit = wrapper.findAll('button').filter((b) => b.text() === 'Create key').at(-1);
    await submit!.trigger('click');
    await flushPromises();

    expect(createApiKey).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="api-key-form-error"]').text()).toMatch(/Pick at least one scope/);
    wrapper.unmount();
  });
});
