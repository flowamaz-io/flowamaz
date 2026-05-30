import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

interface PreferenceResponse {
  key: string;
  value: string | null;
}

export const preferencesService = {
  /** Returns the parsed JSON value for a preference key, or null. */
  async get<T>(key: string): Promise<T | null> {
    const res = await http.get<ApiEnvelope<PreferenceResponse>>(`/api/v1/preferences/${encodeURIComponent(key)}`);
    const value = unwrap(res).value;
    if (!value) return null;
    try {
      return JSON.parse(value) as T;
    } catch {
      return null;
    }
  },

  async set<T>(key: string, value: T): Promise<void> {
    await http.put(`/api/v1/preferences/${encodeURIComponent(key)}`, { value: JSON.stringify(value) });
  },
};
