import { describe, it, expect } from 'vitest';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { toUserFacingError } from '@/utils/error.util';

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
});
