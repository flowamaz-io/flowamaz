import { describe, it, expect } from 'vitest';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { copilotErrorMessage, toUserFacingError } from '@/utils/error.util';

function axiosErrorWith(status: number, data: unknown): AxiosError {
  const response = {
    status,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data,
  } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', response.config, null, response);
}

describe('toUserFacingError', () => {
  it('uses the envelope message from GlobalExceptionMiddleware', () => {
    const e = axiosErrorWith(503, { message: 'AI features are not configured.', code: 'AI_NOT_CONFIGURED' });
    expect(toUserFacingError(e)).toMatchObject({ message: 'AI features are not configured.', code: 'AI_NOT_CONFIGURED' });
  });

  it('uses the `error` field returned by controller BadRequest/UnprocessableEntity bodies', () => {
    const e = axiosErrorWith(422, { error: "We couldn't build a valid workflow.", code: 'WORKFLOW_GENERATION_INVALID' });
    expect(toUserFacingError(e)).toMatchObject({
      message: "We couldn't build a valid workflow.",
      code: 'WORKFLOW_GENERATION_INVALID',
    });
  });

  it('falls back to an actionable HTTP message when the body has no text', () => {
    const e = axiosErrorWith(500, {});
    expect(toUserFacingError(e).message).toContain('HTTP 500');
  });

  it('says a 404 means the item is missing or the link/ID is wrong, not "try again"', () => {
    const r = toUserFacingError(axiosErrorWith(404, {}));
    expect(r.code).toBe('NOT_FOUND');
    expect(r.message).toContain("wasn't found");
    expect(r.message).not.toContain('try again');
  });
});

describe('copilotErrorMessage', () => {
  it('shows the server message verbatim and does not blame the connection', () => {
    const e = axiosErrorWith(502, {
      message: 'AI request failed: Anthropic rejected the platform AI key (HTTP 401). Set Ai__AnthropicPlatformKey.',
      code: 'AI_PROVIDER_AUTH_FAILED',
    });
    const text = copilotErrorMessage(e);
    expect(text).toContain('Anthropic rejected the platform AI key');
    expect(text).not.toContain('Check your connection');
  });

  it('mentions the connection only for a real network failure', () => {
    const e = new AxiosError('Network Error', 'ERR_NETWORK');
    expect(copilotErrorMessage(e)).toContain('Check your connection');
  });
});
