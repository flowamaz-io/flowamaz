import { http } from './api.service';

export const helpService = {
  /** Records whether an article was helpful (👍/👎). */
  async submitFeedback(slug: string, helpful: boolean): Promise<void> {
    await http.post('/api/v1/help/feedback', { slug, helpful });
  },
};
