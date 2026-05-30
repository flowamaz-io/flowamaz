import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface AppNotification {
  id: string;
  type: string;
  title: string;
  message: string;
  actionUrl: string | null;
  isRead: boolean;
  createdAt: string;
}

const base = '/api/v1/notifications';

export const notificationService = {
  async list(unread = false, limit = 20): Promise<AppNotification[]> {
    const res = await http.get<ApiEnvelope<AppNotification[]>>(base, { params: { unread, limit } });
    return unwrap(res);
  },

  async unreadCount(): Promise<number> {
    const res = await http.get<ApiEnvelope<number>>(`${base}/unread-count`);
    return unwrap(res);
  },

  async markRead(id: string): Promise<void> {
    await http.post(`${base}/${id}/read`, {});
  },

  async markAllRead(): Promise<void> {
    await http.post(`${base}/read-all`, {});
  },
};
