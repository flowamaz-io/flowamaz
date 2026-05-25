import axios from 'axios';
import type { ApiErrorEnvelope, UserFacingError } from '@/types';

/** Maps an API error code to a relevant help article path (best-effort). */
const codeToArticle: Record<string, string> = {
  AUTH_INVALID_CREDENTIALS: 'getting-started/cloud-signup',
  AUTH_INVALID_REFRESH: 'getting-started/cloud-signup',
  RATE_LIMIT_EXCEEDED: 'getting-started/cloud-signup',
};

/**
 * Normalises any thrown value (Axios error, API error envelope, generic Error) into a
 * single user-facing shape. Per CLAUDE.md rule 8 every message must be actionable, so we
 * never emit a bare "Something went wrong".
 */
export function toUserFacingError(error: unknown): UserFacingError {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as Partial<ApiErrorEnvelope> | undefined;
    if (data && typeof data.message === 'string' && data.message.length > 0) {
      return {
        message: data.message,
        code: data.code,
        helpArticle: data.code ? codeToArticle[data.code] : undefined,
        correlationId: data.correlationId,
      };
    }
    if (error.code === 'ERR_NETWORK') {
      return {
        message:
          'Could not reach the Flowamaz API. Check your connection and that the backend is running, then try again.',
        code: 'NETWORK_ERROR',
      };
    }
    return {
      message: `Request failed (HTTP ${error.response?.status ?? '???'}). Please try again, or contact support if it persists.`,
      code: 'HTTP_ERROR',
    };
  }

  if (error instanceof Error && error.message) {
    return { message: error.message };
  }

  return {
    message: 'An unexpected error occurred. Please try again — if it keeps happening, contact support@flowamaz.io.',
    code: 'UNKNOWN',
  };
}
