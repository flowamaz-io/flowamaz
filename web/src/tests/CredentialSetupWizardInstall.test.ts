import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import CredentialSetupWizard from '@/components/library/CredentialSetupWizard.vue';
import type { ConnectorDefinition } from '@/services/connector.service';

// The wizard used to say "Connected" without telling the server, so nothing was installed (CC, PR #22).
const installConnector = vi.fn();
vi.mock('@/services/connector.service', () => ({
  connectorService: { installConnector: (...a: unknown[]) => installConnector(...a), initiateOAuth: vi.fn() },
}));
vi.mock('@/composables/useWorkspace', () => ({
  useWorkspace: () => ({ currentWorkspaceId: { value: 'ws-1' } }),
}));

const fileSystem: ConnectorDefinition = {
  id: 'def-1', connectorId: 'file-system', publisherId: 'flowamaz-io', displayName: 'File System', version: '1.0.0',
  category: 'Files', tags: [], manifestJson: '{}', tier: 'Official', isEnabled: true, isInstalled: false,
  installCount: 0, averageRating: 0, ratingCount: 0, isOfficial: true,
};

function mountWizard(install?: boolean) {
  return mount(CredentialSetupWizard, {
    props: { connector: fileSystem, modelValue: true, ...(install === undefined ? {} : { install }) },
    attachTo: document.body,
  });
}

function clickContinue(): void {
  const button = [...document.querySelectorAll('button')].find((b) => /Continue|Install/i.test(b.textContent ?? ''));
  button!.click();
}

describe('CredentialSetupWizard install', () => {
  beforeEach(() => {
    installConnector.mockReset();
    document.body.innerHTML = '';
  });

  it('records the install on the server before saying Connected', async () => {
    installConnector.mockResolvedValue(undefined);
    const w = mountWizard();
    clickContinue();
    await flushPromises();
    expect(installConnector).toHaveBeenCalledWith('ws-1', 'file-system', undefined);
    expect(document.body.textContent).toContain('Connected to File System!');
    w.unmount();
  });

  it('stays on step 1 and says why when the install fails', async () => {
    installConnector.mockRejectedValue(new Error('This needs the Designer role or higher in this workspace.'));
    const w = mountWizard();
    clickContinue();
    await flushPromises();
    expect(document.body.textContent).not.toContain('Connected to File System!');
    expect(document.body.textContent).toContain("File System wasn't installed");
    w.unmount();
  });

  it('does not install when only re-connecting a credential', async () => {
    const w = mountWizard(false);
    clickContinue();
    await flushPromises();
    expect(installConnector).not.toHaveBeenCalled();
    expect(document.body.textContent).toContain('Connected to File System!');
    w.unmount();
  });
});
