import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils';
import PublisherProfileView from '@/views/library/PublisherProfileView.vue';
import LibraryPublisherSection from '@/components/library/LibraryPublisherSection.vue';
import PlatformLibraryReviewView from '@/views/platform/PlatformLibraryReviewView.vue';
import { itemLink, submissionBadge, type PublisherProfile } from '@/services/libraryPublisher.service';

// Library publishers (FUNCTIONAL.md §23.9): profile page, portal section, staff reviews.
const api = vi.hoisted(() => ({
  profile: vi.fn(), portal: vi.fn(), updateProfile: vi.fn(), reviewQueue: vi.fn(), approve: vi.fn(), reject: vi.fn(),
}));
vi.mock('@/services/libraryPublisher.service', async (orig) => ({
  ...(await orig<typeof import('@/services/libraryPublisher.service')>()),
  libraryPublisherService: api,
}));
const staff = { value: true };
vi.mock('@/composables/useAuth', () => ({ useAuth: () => ({ user: { value: { isPlatformStaff: staff.value } } }) }));
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { publisherId: 'acme' } }),
  useRouter: () => ({ push: vi.fn() }),
}));

const stubs = { 'router-link': RouterLinkStub };
const acme: PublisherProfile = {
  publisherId: 'acme', displayName: 'Acme Labs', bio: 'Finance connectors.', websiteUrl: 'https://acme.example', githubLogin: 'acme-labs',
  isVerified: true, isOfficial: false, publishingSince: '2026-10-01T00:00:00Z',
  totals: { items: 2, installs: 15, averageRating: 4.5, ratingCount: 4 },
  items: [
    { kind: 'connector', id: 'invoice-sync', name: 'Invoice Sync', version: '1.2.0', category: 'finance', installs: 12, averageRating: 4.5, ratingCount: 4, deprecatedAt: null, deprecationNote: null, updatedAt: '2026-10-01T00:00:00Z', detailId: null },
    { kind: 'template', id: 'acme-po', name: 'Acme PO', version: '1.0.0', category: 'Finance', installs: 3, averageRating: 0, ratingCount: 0, deprecatedAt: null, deprecationNote: null, updatedAt: '2026-10-02T00:00:00Z', detailId: 't-1' },
  ],
};

const axios409 = (message: string) => Object.assign(new Error('Request failed with status code 400'), {
  isAxiosError: true, response: { status: 400, data: { code: 'LIBRARY_PROFILE_INVALID', message } },
});

describe('libraryPublisher helpers', () => {
  it('link items to their pages and label statuses', () => {
    expect(itemLink(acme.items[0])).toBe('/library/connectors/invoice-sync');
    expect(itemLink(acme.items[1])).toBe('/library/templates/t-1');
    expect(submissionBadge('approved').label).toBe('Live');
    expect(submissionBadge('pending').label).toBe('In review');
    expect(submissionBadge('rejected').variant).toBe('danger');
  });
});

describe('PublisherProfileView', () => {
  beforeEach(() => { Object.values(api).forEach((f) => f.mockReset()); });

  it('shows who the publisher is and what they publish', async () => {
    api.profile.mockResolvedValue(acme);
    const w = mount(PublisherProfileView, { global: { stubs } });
    await flushPromises();
    expect(api.profile).toHaveBeenCalledWith('acme');
    expect(w.text()).toContain('Acme Labs');
    expect(w.find('[data-testid="publisher-verified"]').exists()).toBe(true);
    expect(w.text()).toContain('4.5 ★');
    expect(w.find('[data-testid="publisher-items-connector"]').text()).toContain('Invoice Sync');
    expect(w.findAll('a[target="_blank"]').map((a) => a.attributes('rel'))).toEqual(['noopener noreferrer nofollow', 'noopener noreferrer nofollow']);
  });

  it('explains an unknown publisher', async () => {
    api.profile.mockRejectedValue(Object.assign(new Error('x'), {
      isAxiosError: true, response: { status: 404, data: { code: 'LIBRARY_PUBLISHER_NOT_FOUND', message: 'nope' } },
    }));
    const w = mount(PublisherProfileView, { global: { stubs } });
    await flushPromises();
    expect(w.text()).toContain('No such publisher');
  });
});

describe('LibraryPublisherSection', () => {
  beforeEach(() => { Object.values(api).forEach((f) => f.mockReset()); });

  it('lists items and submissions with what happens next', async () => {
    api.portal.mockResolvedValue({
      publisherId: 'acme', hasProfile: true, profile: acme,
      submissions: [
        { id: 's1', kind: 'connector', name: 'Invoice Sync', libraryId: 'acme/invoice-sync', status: 'merged', statusText: 'Merged. It goes live with the next Library sync, within 15 minutes.', pullRequestUrl: 'https://github.com/flowamaz-io/connectors/pull/7', reviewerNotes: null, submittedAt: '2026-11-01T00:00:00Z', decidedAt: null, checkedAt: '2026-11-02T00:00:00Z' },
        { id: 's2', kind: 'template', name: 'Old PO', libraryId: 'old-po', status: 'rejected', statusText: 'Not accepted.', pullRequestUrl: null, reviewerNotes: 'No approval step.', submittedAt: '2026-10-01T00:00:00Z', decidedAt: '2026-10-03T00:00:00Z', checkedAt: null },
      ],
    });
    const w = mount(LibraryPublisherSection, { global: { stubs } });
    await flushPromises();
    expect(w.text()).toContain('In the Library (2)');
    expect(w.text()).toContain('Merged. It goes live');
    expect(w.text()).toContain("Reviewer's note: No approval step.");
    expect(w.text()).toContain('Not accepted');
  });

  it("shows the server's message when saving the profile fails", async () => {
    api.portal.mockResolvedValue({ publisherId: 'acme', hasProfile: false, profile: { ...acme, bio: '', items: [] }, submissions: [] });
    api.updateProfile.mockRejectedValue(axios409('The website must be a full https:// address of up to 200 characters, e.g. https://acme.example.'));
    const w = mount(LibraryPublisherSection, { global: { stubs } });
    await flushPromises();
    expect(w.find('[data-testid="library-publisher-no-items"]').exists()).toBe(true);
    expect(w.find('[data-testid="library-publisher-no-submissions"]').exists()).toBe(true);
    await w.get('[data-testid="library-publisher-edit"]').trigger('click');
    await w.get('[data-testid="library-publisher-save"]').trigger('click');
    await flushPromises();
    expect(w.text()).toContain('full https:// address');
    expect(w.text()).not.toContain('status code');
  });
});

describe('PlatformLibraryReviewView', () => {
  beforeEach(() => { Object.values(api).forEach((f) => f.mockReset()); staff.value = true; });

  const template = { id: 't-9', name: 'Vendor intake', slug: 'vendor-intake', version: '0.1.0', category: 'Ops', description: 'Onboard vendors', publisherId: 'acme', orgName: 'Acme Ltd', yamlContent: 'apiVersion: flowamaz/v1', submittedAt: '2026-11-10T00:00:00Z' };

  it('needs a note to reject, then removes the decided template', async () => {
    api.reviewQueue.mockResolvedValue({ templates: [template], connectorPullRequests: [] });
    api.reject.mockResolvedValue(template);
    const w = mount(PlatformLibraryReviewView, { global: { stubs } });
    await flushPromises();
    await w.get('[data-testid="review-reject-vendor-intake"]').trigger('click');
    expect(api.reject).not.toHaveBeenCalled();
    expect(w.text()).toContain('at least 10 characters');
    await w.get('textarea').setValue('The approval step has no assignee.');
    await w.get('[data-testid="review-reject-vendor-intake"]').trigger('click');
    await flushPromises();
    expect(api.reject).toHaveBeenCalledWith('t-9', 'The approval step has no assignee.');
    expect(w.find('[data-testid="review-template-vendor-intake"]').exists()).toBe(false);
    expect(w.text()).toContain('rejected — the publisher has your note');
  });

  it('is closed to everyone but staff', async () => {
    staff.value = false;
    const w = mount(PlatformLibraryReviewView, { global: { stubs } });
    await flushPromises();
    expect(api.reviewQueue).not.toHaveBeenCalled();
    expect(w.text()).toContain('This page is for Flowamaz staff');
  });
});
