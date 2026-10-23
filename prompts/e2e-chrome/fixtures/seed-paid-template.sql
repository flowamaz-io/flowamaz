-- Seeds one PAID community template ("E2E Paid Expense Flow", USD 49 one-time) so the purchase gate
-- can be checked in the UI without Stripe. Safe to run twice. Remove with the DELETE at the bottom.
INSERT INTO workflow_templates (id, name, slug, description, category, is_official, org_id, yaml_content,
    preview_image_url, install_count, average_rating, tags, version, is_active, review_status,
    pricing_model, price_cents, currency, created_at, updated_at, is_deleted)
SELECT '00000000-0000-0000-00e2-000000000049', 'E2E Paid Expense Flow', 'e2e-paid-expense-flow',
       'Seeded by the Chrome E2E run to check that paid workflows stay locked until bought.', 'Finance',
       false, '00000000-0000-0000-00e2-0000000000aa', yaml_content, NULL, 0, 0, '["e2e"]', '1.0.0', true, 'approved',
       'one_time', 4900, 'usd', now(), now(), false
FROM workflow_templates WHERE id = '00000000-0000-0000-0002-000000000006'
ON CONFLICT (id) DO NOTHING;

-- Cleanup (run after the test):
-- DELETE FROM template_purchases WHERE template_id = '00000000-0000-0000-00e2-000000000049';
-- DELETE FROM workflow_templates WHERE id = '00000000-0000-0000-00e2-000000000049';
