import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach, vi } from 'vitest';
import { clearCsrfToken } from '@/lib/api.ts';

beforeEach(() => {
  // jsdom does not implement window scrolling.
  const scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => {});
  return () => scrollTo.mockRestore();
});

afterEach(clearCsrfToken);
