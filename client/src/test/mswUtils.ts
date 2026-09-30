import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { afterAll, afterEach, beforeAll } from 'vitest';

// Create a shared MSW server instance that can be used across all tests
export const testServer = setupServer(
  http.get('/api/teams', () => HttpResponse.json([]))
);

// Global setup for MSW server
// This will be automatically called when imported in test files
beforeAll(() => {
  testServer.listen({ onUnhandledRequest: 'error' });
});

afterEach(() => {
  testServer.resetHandlers();
});

afterAll(() => {
  testServer.close();
});

export { testServer as server };
