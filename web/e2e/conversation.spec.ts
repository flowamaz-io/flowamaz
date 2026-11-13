import { expect, test } from '@playwright/test';
import { createWorkflowFor, setupReadyOrg, uniqueSuffix } from './fixtures/test-factories';

/**
 * Conversation → Workflow (S81). Paste a Slack thread → review what was extracted → generate → canvas.
 * The suite never depends on a live AI key, so the two AI-backed calls are answered by the test: the preview with
 * a fixed extraction, and /create by checking it received the *edited* extraction (not the raw thread) and
 * answering with a real workflow, which the editor then opens.
 */
test.describe('Conversation → Workflow (S81)', () => {
  test('S81: paste a Slack thread → confirm the extraction → generated workflow opens', async ({ page }) => {
    const ready = await setupReadyOrg(page);
    const generated = await createWorkflowFor(ready, `conv-${uniqueSuffix()}`);

    await page.route('**/workflows/conversation/preview', async (route) => {
      const body = route.request().postDataJSON() as { text: string; sourceType: string };
      expect(body.sourceType).toBe('slack');
      expect(body.text).toContain('approve up to $5,000');
      await route.fulfill({
        json: {
          data: {
            extraction: {
              workflowName: 'Purchase approval',
              summary: 'Approves purchase requests by amount',
              trigger: 'A purchase request is submitted',
              participants: [{ name: 'Anita', messageCount: 1 }, { name: 'Rajan', messageCount: 1 }],
              steps: [
                { title: 'Submit purchase request', owner: 'Anita', system: null, saidBy: 'Anita' },
                { title: 'Approve the request', owner: 'Rajan', system: null, saidBy: 'Rajan' },
              ],
              decisions: [{ condition: 'amount above $5,000', outcome: 'CFO approves', saidBy: 'Rajan' }],
              approvers: [{ who: 'Rajan', when: 'up to $5,000' }],
              agreements: ['Rajan approves up to $5,000'],
              systems: [],
              openQuestions: ['Who approves when the CFO is away?'],
              confidence: 0.82,
            },
            warnings: ['Left undecided in the thread: Who approves when the CFO is away?'],
            tokensUsed: 500,
          },
        },
      });
    });

    let createBody: { method?: string; conversationText?: string; conversationExtraction?: { steps: { title: string; system: string | null }[] } } = {};
    await page.route('**/workflows/create', async (route) => {
      createBody = route.request().postDataJSON() as typeof createBody;
      await route.fulfill({ json: { data: { workflowId: generated.id, workflowName: generated.name, workflowSlug: 'x', method: 'conversation' } } });
    });

    await page.goto('/workflows/new');
    await page.getByText('Paste a conversation').click();
    await page.getByRole('radio', { name: 'Slack' }).click();
    await page.getByLabel('Conversation text').fill(
      '[{"user":"anita","text":"Can someone approve this $8,000 request?"},{"user":"rajan","text":"I approve up to $5,000; CFO above that"}]',
    );
    await page.getByRole('button', { name: 'Extract process' }).click();

    // Review: what was read out, and what to check.
    await expect(page.getByText('82% confident')).toBeVisible();
    await expect(page.getByText('Left undecided in the thread: Who approves when the CFO is away?')).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Step 1', exact: true })).toHaveValue('Submit purchase request');

    // Correct it: the PO is raised in SAP by a step the thread implied.
    await page.getByRole('button', { name: 'Add step' }).click();
    await page.getByRole('textbox', { name: 'Step 3', exact: true }).fill('Raise the purchase order');
    await page.getByRole('textbox', { name: 'Step 3 system' }).fill('SAP');
    await page.getByRole('button', { name: 'Generate workflow' }).click();

    await page.waitForURL(new RegExp(`/workflows/${generated.id}/edit`));
    expect(createBody.method).toBe('conversation');
    expect(createBody.conversationText).toBeUndefined();
    expect(createBody.conversationExtraction?.steps.map((s) => s.title)).toEqual([
      'Submit purchase request',
      'Approve the request',
      'Raise the purchase order',
    ]);
    expect(createBody.conversationExtraction?.steps[2].system).toBe('SAP');
  });
});
