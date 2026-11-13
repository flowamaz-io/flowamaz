import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

// Library publishers (FUNCTIONAL.md §23.9). Mirrors Flowamaz.Core.Models.PublisherModels.

export type PublisherItemKind = 'connector' | 'template' | 'form' | 'pack';

export interface PublisherItem {
  kind: PublisherItemKind;
  id: string;
  name: string;
  version: string;
  category: string;
  installs: number;
  averageRating: number;
  ratingCount: number;
  deprecatedAt: string | null;
  deprecationNote: string | null;
  updatedAt: string;
  /** The id the item's page uses when it differs from id (templates). */
  detailId: string | null;
}

export interface PublisherProfile {
  publisherId: string;
  displayName: string;
  bio: string;
  websiteUrl: string | null;
  githubLogin: string | null;
  isVerified: boolean;
  isOfficial: boolean;
  publishingSince: string | null;
  totals: { items: number; installs: number; averageRating: number; ratingCount: number };
  items: PublisherItem[];
}

export type SubmissionStatus = 'pending' | 'merged' | 'live' | 'approved' | 'rejected';

export interface PublisherSubmission {
  id: string;
  kind: 'connector' | 'template';
  name: string;
  libraryId: string | null;
  status: SubmissionStatus;
  statusText: string;
  pullRequestUrl: string | null;
  reviewerNotes: string | null;
  submittedAt: string;
  decidedAt: string | null;
  checkedAt: string | null;
}

export interface PublisherPortal {
  publisherId: string;
  hasProfile: boolean;
  profile: PublisherProfile;
  submissions: PublisherSubmission[];
}

export interface UpdatePublisherProfileRequest {
  displayName: string;
  bio?: string;
  websiteUrl?: string;
  githubLogin?: string;
}

export interface LibraryReviewTemplate {
  id: string;
  name: string;
  slug: string;
  version: string;
  category: string;
  description: string;
  publisherId: string;
  orgName: string;
  yamlContent: string;
  submittedAt: string;
}

export interface LibraryReviewQueue {
  templates: LibraryReviewTemplate[];
  connectorPullRequests: PublisherSubmission[];
}

export const libraryPublisherService = {
  async profile(publisherId: string): Promise<PublisherProfile> {
    return unwrap(await http.get<ApiEnvelope<PublisherProfile>>(`/api/v1/library/publishers/${encodeURIComponent(publisherId)}`));
  },

  /** Org owner only. */
  async portal(): Promise<PublisherPortal> {
    return unwrap(await http.get<ApiEnvelope<PublisherPortal>>('/api/v1/publisher/portal'));
  },

  async updateProfile(request: UpdatePublisherProfileRequest): Promise<PublisherProfile> {
    return unwrap(await http.put<ApiEnvelope<PublisherProfile>>('/api/v1/publisher/profile', request));
  },

  /** Flowamaz staff only. */
  async reviewQueue(): Promise<LibraryReviewQueue> {
    return unwrap(await http.get<ApiEnvelope<LibraryReviewQueue>>('/api/v1/platform/library/reviews'));
  },

  async approve(templateId: string, notes?: string): Promise<LibraryReviewTemplate> {
    return unwrap(await http.post<ApiEnvelope<LibraryReviewTemplate>>(`/api/v1/platform/library/reviews/templates/${templateId}/approve`, { notes }));
  },

  async reject(templateId: string, notes: string): Promise<LibraryReviewTemplate> {
    return unwrap(await http.post<ApiEnvelope<LibraryReviewTemplate>>(`/api/v1/platform/library/reviews/templates/${templateId}/reject`, { notes }));
  },
};

export const KIND_LABELS: Record<PublisherItemKind, string> = {
  connector: 'Connectors', template: 'Templates', form: 'Forms', pack: 'Packs',
};

/** Where an item opens in the app. */
export function itemLink(item: PublisherItem): string {
  switch (item.kind) {
    case 'connector': return `/library/connectors/${item.id}`;
    case 'template': return item.detailId ? `/library/templates/${item.detailId}` : '/library?tab=templates';
    case 'form': return '/library?tab=forms';
    default: return '/library?tab=packs';
  }
}

/** "Live", "Waiting for review" … for a submission badge. */
export function submissionBadge(status: SubmissionStatus): { label: string; variant: 'accent' | 'amber' | 'blue' | 'danger' } {
  switch (status) {
    case 'live':
    case 'approved': return { label: 'Live', variant: 'accent' };
    case 'merged': return { label: 'Merged', variant: 'blue' };
    case 'rejected': return { label: 'Not accepted', variant: 'danger' };
    default: return { label: 'In review', variant: 'amber' };
  }
}
