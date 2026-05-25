import { afterAll, afterEach, beforeAll, vi } from 'vitest';
import { server } from './mocks/server';

// Stub matchMedia / clipboard not present in jsdom.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }),
});

if (!navigator.clipboard) {
  Object.defineProperty(navigator, 'clipboard', {
    writable: true,
    value: { writeText: vi.fn().mockResolvedValue(undefined) },
  });
}

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
