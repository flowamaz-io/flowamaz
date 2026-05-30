import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface TemplateListItem {
  id: string;
  name: string;
  slug: string;
  description: string;
  category: string;
  isOfficial: boolean;
  previewImageUrl: string | null;
  installCount: number;
  averageRating: number;
  tags: string[];
  version: string;
  createdAt: string;
}

export interface TemplateNodeSummary {
  nodeType: string;
  count: number;
}

export interface TemplateDetail extends TemplateListItem {
  yamlContent: string;
  nodeSummary: TemplateNodeSummary[];
}

export interface TemplatePage {
  items: TemplateListItem[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

export interface PublishTemplateRequest {
  workflowId: string;
  name: string;
  description: string;
  category: string;
  tags: string[];
  previewImageUrl: string | null;
}

export interface PublishTemplateResult {
  templateId: string;
  slug: string;
  reviewStatus: string;
}

export const templateService = {
  async list(params: {
    category?: string;
    search?: string;
    page?: number;
    pageSize?: number;
  } = {}): Promise<TemplatePage> {
    const res = await http.get<ApiEnvelope<TemplatePage>>('/api/v1/templates', {
      params: {
        category: params.category,
        search: params.search,
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 12,
      },
    });
    return unwrap(res);
  },

  async get(id: string): Promise<TemplateDetail> {
    const res = await http.get<ApiEnvelope<TemplateDetail>>(`/api/v1/templates/${id}`);
    return unwrap(res);
  },

  async install(workspaceId: string, templateId: string, name: string): Promise<string> {
    const res = await http.post<ApiEnvelope<{ workflowId: string }>>(
      `/api/v1/workspaces/${workspaceId}/templates/${templateId}/install`,
      { name },
    );
    return unwrap(res).workflowId;
  },

  async publish(workspaceId: string, request: PublishTemplateRequest): Promise<PublishTemplateResult> {
    const res = await http.post<ApiEnvelope<PublishTemplateResult>>(
      `/api/v1/workspaces/${workspaceId}/templates/publish`,
      request,
    );
    return unwrap(res);
  },
};
