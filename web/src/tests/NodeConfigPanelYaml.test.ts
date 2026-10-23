import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import NodeConfigPanel, { type ConfigNode } from '@/components/canvas/NodeConfigPanel.vue';
import { connectorService } from '@/services/connector.service';
import { credentialService } from '@/services/credential.service';

// Nodes exactly as samples/loan-approval/loan-approval.workflow.yaml writes them (snake_case
// spec keys). Opening the panel must show these values, and "Save node" with no edits must
// hand back the very same config (B13 — silent config loss).

function mountPanel(node: ConfigNode, extra: Record<string, unknown> = {}) {
  return mount(NodeConfigPanel, {
    props: { open: true, node, workspaceId: 'ws-1', variables: [], ...extra },
    global: { stubs: { RouterLink: true } },
  });
}

async function saveNode(wrapper: VueWrapper): Promise<{ label: string; config: Record<string, unknown> }> {
  await wrapper.get('[data-test="save-node"]').trigger('click');
  return wrapper.emitted('save')!.at(-1)![0] as { label: string; config: Record<string, unknown> };
}

function selectValue(wrapper: VueWrapper, testId: string): string {
  return (wrapper.get(`[data-test="${testId}"]`).element as HTMLSelectElement).value;
}

function inputValue(wrapper: VueWrapper, testId: string): string {
  return (wrapper.get(`[data-test="${testId}"]`).element as HTMLInputElement).value;
}

const LO_ACCEPTANCE: ConfigNode = {
  id: 'lo-acceptance',
  type: 'human-gate',
  label: 'Customer accepts Letter of Offer',
  config: {
    delivery_channel: 'Portal',
    gate_label: 'Accept Letter of Offer',
    assigned_to_variable: 'applicant_email',
    timeout_hours: 168,
    on_timeout: 'auto-reject',
  },
};

beforeEach(() => {
  vi.restoreAllMocks();
  vi.spyOn(credentialService, 'list').mockResolvedValue([]);
});

describe('HumanGateConfig with YAML (snake_case) keys', () => {
  it('shows timeout_hours, on_timeout and assigned_to_variable from the YAML', () => {
    const wrapper = mountPanel(LO_ACCEPTANCE);

    expect(selectValue(wrapper, 'gate-assignee-type')).toBe('variable');
    expect(inputValue(wrapper, 'gate-assignee')).toBe('applicant_email');
    // 168 hours reads as 7 days.
    expect(inputValue(wrapper, 'gate-timeout-value')).toBe('7');
    expect(selectValue(wrapper, 'gate-timeout-unit')).toBe('days');
    expect(selectValue(wrapper, 'gate-on-timeout')).toBe('auto-reject');
    // delivery_channel Portal: no extra channel is ticked.
    for (const ch of ['email', 'slack', 'teams']) {
      expect((wrapper.get(`[data-test="gate-channel-${ch}"]`).element as HTMLInputElement).checked).toBe(false);
    }
  });

  it('saving without changes returns the config untouched', async () => {
    const wrapper = mountPanel(LO_ACCEPTANCE);
    const saved = await saveNode(wrapper);
    expect(saved.config).toEqual(LO_ACCEPTANCE.config);
    expect(saved.label).toBe(LO_ACCEPTANCE.label);
  });

  it('edits the snake_case keys in place and never adds shadowed canvas keys', async () => {
    const wrapper = mountPanel(LO_ACCEPTANCE);

    await wrapper.get('[data-test="gate-timeout-value"]').setValue('10');
    await wrapper.get('[data-test="gate-on-timeout"]').setValue('escalate');
    await wrapper.get('[data-test="gate-assignee"]').setValue('customer_email');
    const saved = await saveNode(wrapper);

    expect(saved.config).toEqual({
      ...LO_ACCEPTANCE.config,
      timeout_hours: 240,
      on_timeout: 'escalate',
      assigned_to_variable: 'customer_email',
    });
    expect(saved.config).not.toHaveProperty('timeoutValue');
    expect(saved.config).not.toHaveProperty('onTimeout');
    expect(saved.config).not.toHaveProperty('assignee');
  });

  it('switching the unit keeps the number and round-trips to timeout_hours', async () => {
    const wrapper = mountPanel(LO_ACCEPTANCE);
    await wrapper.get('[data-test="gate-timeout-unit"]').setValue('hours');
    expect(inputValue(wrapper, 'gate-timeout-value')).toBe('7');
    const saved = await saveNode(wrapper);
    expect(saved.config.timeout_hours).toBe(7);
  });

  it('a non-day timeout shows in hours', () => {
    const wrapper = mountPanel({ ...LO_ACCEPTANCE, config: { timeout_hours: 30, on_timeout: 'fail' } });
    expect(inputValue(wrapper, 'gate-timeout-value')).toBe('30');
    expect(selectValue(wrapper, 'gate-timeout-unit')).toBe('hours');
  });

  it('normalises on_timeout like the engine (reject / AUTO_APPROVE)', () => {
    expect(selectValue(mountPanel({ ...LO_ACCEPTANCE, config: { on_timeout: 'reject' } }), 'gate-on-timeout')).toBe('auto-reject');
    expect(selectValue(mountPanel({ ...LO_ACCEPTANCE, config: { on_timeout: 'AUTO_APPROVE' } }), 'gate-on-timeout')).toBe('auto-approve');
  });

  it('a single channel toggle keeps delivery_channel; several switch to the channels map', async () => {
    const wrapper = mountPanel(LO_ACCEPTANCE);
    await wrapper.get('[data-test="gate-channel-email"]').trigger('change');
    let saved = await saveNode(wrapper);
    expect(saved.config.delivery_channel).toBe('Email');
    expect(saved.config).not.toHaveProperty('channels');

    await wrapper.get('[data-test="gate-channel-teams"]').trigger('change');
    saved = await saveNode(wrapper);
    expect(saved.config).not.toHaveProperty('delivery_channel');
    expect(saved.config.channels).toEqual({ email: true, slack: false, teams: true });
  });

  it('reads slack_channel / escalate_to from the YAML', async () => {
    const wrapper = mountPanel({
      ...LO_ACCEPTANCE,
      config: { delivery_channel: 'Slack', slack_channel: '#loans', on_timeout: 'escalate', escalate_to: 'boss@example.com' },
    });
    await flushPromises();
    const inputs = wrapper.findAll('input').map((i) => (i.element as HTMLInputElement).value);
    expect(inputs).toContain('#loans');
    expect(inputs).toContain('boss@example.com');
  });

  it('escalate-to: empty asks for a recipient, an invalid address says it must be a valid email (O6)', async () => {
    const wrapper = mountPanel({ ...LO_ACCEPTANCE, config: { on_timeout: 'escalate' } });
    expect(wrapper.get('[data-test="gate-escalate-error"]').text()).toContain('Enter who should get the approval');

    await wrapper.get('[data-test="gate-escalate-to"]').setValue('not-an-email');
    expect(wrapper.get('[data-test="gate-escalate-error"]').text()).toContain("isn't a valid email address");

    await wrapper.get('[data-test="gate-escalate-to"]').setValue('director@example.com');
    expect(wrapper.find('[data-test="gate-escalate-error"]').exists()).toBe(false);
    const saved = await saveNode(wrapper);
    expect(saved.config).toEqual({ on_timeout: 'escalate', escalate_to: 'director@example.com' });
  });

  it('canvas-authored gates keep using the canvas keys', async () => {
    const node: ConfigNode = {
      id: 'gate-1', type: 'human-gate', label: 'Approval',
      config: { assigneeType: 'role', assignee: 'Designer', timeoutValue: 2, timeoutUnit: 'days', onTimeout: 'auto-approve' },
    };
    const wrapper = mountPanel(node);
    expect(inputValue(wrapper, 'gate-timeout-value')).toBe('2');
    expect(selectValue(wrapper, 'gate-timeout-unit')).toBe('days');
    expect(selectValue(wrapper, 'gate-on-timeout')).toBe('auto-approve');
    expect(selectValue(wrapper, 'gate-assignee')).toBe('Designer');

    await wrapper.get('[data-test="gate-timeout-value"]').setValue('3');
    const saved = await saveNode(wrapper);
    expect(saved.config).toEqual({ ...node.config, timeoutValue: 3, timeoutUnit: 'days' });
    expect(saved.config).not.toHaveProperty('timeout_hours');
  });

  it('an unconfigured gate shows the engine default (48 hours, fail) and saves nothing extra', async () => {
    const node: ConfigNode = { id: 'gate-2', type: 'human-gate', label: 'Approval', config: {} };
    const wrapper = mountPanel(node);
    expect(Number(inputValue(wrapper, 'gate-timeout-value')) * (selectValue(wrapper, 'gate-timeout-unit') === 'days' ? 24 : 1)).toBe(48);
    expect(selectValue(wrapper, 'gate-on-timeout')).toBe('fail');
    const saved = await saveNode(wrapper);
    expect(saved.config).toEqual({});
  });
});

describe('Other config panels with YAML keys', () => {
  it('action: an http-rest step keeps connector_id / operation / inputs map', async () => {
    vi.spyOn(connectorService, 'getAllConnectors').mockResolvedValue([]);
    const config = {
      connector_id: 'http-rest',
      operation: 'POST',
      inputs: { url: 'http://localhost:4010/letters-of-offer', body: '{"amount":{{ loan_amount }}}' },
    };
    const node: ConfigNode = { id: 'create-lo', type: 'action', label: 'Create Letter of Offer', config };
    const wrapper = mountPanel(node);
    await flushPromises();

    // The connector couldn't be loaded: it's shown as-is, not as "Select a connector".
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('http-rest');
    expect(wrapper.find('[data-test="action-unknown-connector"]').exists()).toBe(true);

    // The Inputs tab shows the map as rows and doesn't turn it into a list.
    const inputsTab = wrapper.findAll('button').find((b) => b.text() === 'Inputs')!;
    await inputsTab.trigger('click');
    const values = wrapper.findAll('input').map((i) => (i.element as HTMLInputElement).value);
    expect(values).toContain('url');
    expect(values).toContain('http://localhost:4010/letters-of-offer');

    const saved = await saveNode(wrapper);
    expect(saved.config).toEqual(config);
  });

  it('action: editing a row in the Inputs tab keeps inputs a map', async () => {
    vi.spyOn(connectorService, 'getAllConnectors').mockResolvedValue([]);
    const config = { connector_id: 'http-rest', operation: 'POST', inputs: { url: 'http://a', timeout_seconds: 30 } };
    const wrapper = mountPanel({ id: 'a', type: 'action', label: 'A', config });
    await flushPromises();
    await wrapper.findAll('button').find((b) => b.text() === 'Inputs')!.trigger('click');
    const urlValue = wrapper.findAll('input').find((i) => (i.element as HTMLInputElement).value === 'http://a')!;
    await urlValue.setValue('http://b');
    const saved = await saveNode(wrapper);
    // timeout_seconds stays a number because its text wasn't edited.
    expect(saved.config.inputs).toEqual({ url: 'http://b', timeout_seconds: 30 });
  });

  it('ai: shows system_prompt / prompt_template / max_tokens and writes them back in place', async () => {
    const config = { provider: 'anthropic', model: 'claude-sonnet-4-6', system_prompt: 'Be brief', prompt_template: 'Summarise {{ x }}', max_tokens: 2048 };
    const wrapper = mountPanel({ id: 'ai-1', type: 'ai', label: 'Summarise', config });
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('Be brief');
    expect(wrapper.text()).toContain('2048');
    const selects = wrapper.findAll('select').map((s) => (s.element as HTMLSelectElement).value);
    expect(selects).toEqual(['Anthropic', 'claude-sonnet-4-6']);

    let saved = await saveNode(wrapper);
    expect(saved.config).toEqual(config);

    await wrapper.find('textarea').setValue('Be thorough');
    saved = await saveNode(wrapper);
    expect(saved.config).toEqual({ ...config, system_prompt: 'Be thorough' });
    expect(saved.config).not.toHaveProperty('systemPrompt');
  });

  it('router: branches with label/condition are shown and saved unchanged', async () => {
    const config = {
      branches: [
        { label: 'Up to 50K', condition: '{{ loan_amount }} <= 50000' },
        { label: 'Above 50K', condition: '{{ loan_amount }} > 50000' },
      ],
    };
    const wrapper = mountPanel({ id: 'l1-limit', type: 'router', label: 'Within 50K?', config });
    const values = wrapper.findAll('input').map((i) => (i.element as HTMLInputElement).value);
    expect(values).toContain('Up to 50K');
    expect(values).toContain('{{ loan_amount }} > 50000');
    const saved = await saveNode(wrapper);
    expect(saved.config).toEqual(config);
  });

  it('trigger: shows the workflow trigger type from spec.trigger (form), not "manual"', async () => {
    const wrapper = mountPanel(
      { id: 'application-submitted', type: 'trigger', label: 'Loan application submitted', config: {} },
      { workflowTriggerType: 'form' },
    );
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('form');
    const saved = await saveNode(wrapper);
    expect(saved.config).toEqual({});
  });

  it('end: an outcome outside the list is shown, and saved unchanged', async () => {
    const config = { outcome: 'declined', outcome_description: 'Customer declined' };
    const wrapper = mountPanel({ id: 'end-declined', type: 'end', label: 'Offer declined', config });
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('declined');
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('Customer declined');
    const saved = await saveNode(wrapper);
    expect(saved.config).toEqual(config);
  });
});
