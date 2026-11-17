import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import ActionConfig from '../src/components/canvas/config/ActionConfig.vue';
import GateFormEditor from '../src/components/canvas/config/GateFormEditor.vue';
import { WORKBENCH_HOST, type WorkbenchApi } from '../src';

// The canvas's step settings read connectors, credentials and Library forms through the host's WorkbenchApi.
// Those calls are optional: a host without them still gets working settings, minus the lists.

const manifest = JSON.stringify({
  auth: { credential_types: ['ApiKey'] },
  operations: [{ id: 'send', name: 'Send a message' }, { id: 'ping', internal: true }],
});

function host(api: Partial<WorkbenchApi>) {
  return { global: { provide: { [WORKBENCH_HOST as symbol]: { api: api as WorkbenchApi } } } };
}

describe('step settings on the canvas', () => {
  it('lists the connectors and credentials the API offers', async () => {
    const listCredentials = vi.fn(async () => [
      { id: 'c1', name: 'slack-bot', connectorId: 'slack', isActive: true },
      { id: 'c2', name: 'old-token', connectorId: 'slack', isActive: false },
    ]);
    const w = mount(ActionConfig, {
      props: { config: { connector: 'slack' }, variables: [] },
      ...host({
        listConnectors: async () => [
          { connectorId: 'slack', displayName: 'Slack', manifestJson: manifest, isOfficial: true },
          { connectorId: 'other', displayName: 'Not installed', manifestJson: '{}', isOfficial: false, isInstalled: false },
        ],
        listCredentials,
      }),
    });
    await flushPromises();

    const text = w.text();
    expect(text).toContain('Slack');
    expect(text).not.toContain('Not installed');
    expect(text).toContain('Send a message');
    expect(listCredentials).toHaveBeenCalledWith('slack');
    expect(w.findAll('option').map((o) => o.text())).toContain('slack-bot');
    expect(w.findAll('option').map((o) => o.text())).not.toContain('old-token');
  });

  it('offers the Library only when the API has one', async () => {
    const config = { form: { fields: [{ key: 'a', label: 'A', type: 'text' }] } };
    const without = mount(GateFormEditor, { props: { config }, ...host({}) });
    expect(without.find('[data-test="gate-form-library"]').exists()).toBe(false);
    expect(without.find('[data-test="gate-form-save"]').exists()).toBe(false);

    const saveLibraryForm = vi.fn(async (form: { name: string }) => ({
      id: 'f1', slug: 'f', name: form.name, isOfficial: false, publisher: null, fields: [],
    }));
    const withLibrary = mount(GateFormEditor, {
      props: { config },
      ...host({ listLibraryForms: async () => [], saveLibraryForm }),
    });
    expect(withLibrary.find('[data-test="gate-form-library"]').exists()).toBe(true);
    expect(withLibrary.find('[data-test="gate-form-save"]').exists()).toBe(true);
  });
});
