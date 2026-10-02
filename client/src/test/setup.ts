import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { clearCsrfToken } from '@/lib/api.ts';

afterEach(clearCsrfToken);
