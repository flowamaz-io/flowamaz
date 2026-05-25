import { http, HttpResponse } from 'msw';

const BASE = 'http://localhost:5000';

function envelope<T>(data: T, statusCode = 200) {
  return { success: statusCode < 300, statusCode, data, correlationId: 'test-correlation' };
}

const USER = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'jane@acme.com',
  name: 'Jane Doe',
  orgId: '22222222-2222-2222-2222-222222222222',
  orgSlug: 'acme',
};

export const handlers = [
  http.post(`${BASE}/api/v1/auth/login`, async ({ request }) => {
    const body = (await request.json()) as { email: string; password: string; orgSlug: string };
    if (body.password === 'wrong') {
      return HttpResponse.json(
        {
          success: false,
          statusCode: 401,
          code: 'AUTH_INVALID_CREDENTIALS',
          message: 'Email or password is incorrect. Check your details and try again.',
          correlationId: 'test',
        },
        { status: 401 },
      );
    }
    return HttpResponse.json(envelope({ accessToken: 'access-token-1', expiresIn: 900, user: USER }));
  }),

  http.post(`${BASE}/api/v1/auth/refresh`, () =>
    HttpResponse.json(envelope({ accessToken: 'refreshed-token', expiresIn: 900 })),
  ),

  http.post(`${BASE}/api/v1/auth/logout`, () => HttpResponse.json(envelope({ loggedOut: true }))),

  http.get(`${BASE}/api/v1/auth/me`, () =>
    HttpResponse.json(
      envelope({
        ...USER,
        workspaces: [
          { id: '33333333-3333-3333-3333-333333333333', slug: 'ops', name: 'Operations', role: 'Admin' },
        ],
      }),
    ),
  ),

  http.get(`${BASE}/api/v1/workspaces`, () =>
    HttpResponse.json(
      envelope({
        data: [{ id: '33333333-3333-3333-3333-333333333333', name: 'Operations', slug: 'ops', role: 'Admin' }],
        pagination: { page: 1, pageSize: 20, total: 1, totalPages: 1 },
      }),
    ),
  ),
];
