import { describe, it, expect } from 'vitest';
import { webhookUrl } from '@/services/webhook.service';

describe('webhookUrl', () => {
  it('uses the page origin on a local deployment', () => {
    expect(webhookUrl('abc', '', 'http://localhost:5173')).toBe('http://localhost:5173/webhooks/abc');
  });

  it('prefers the configured API base URL and trims trailing slashes', () => {
    expect(webhookUrl('abc', 'https://flowamaz.acme.internal/', 'http://localhost:5173')).toBe(
      'https://flowamaz.acme.internal/webhooks/abc',
    );
  });
});
