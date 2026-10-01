import { fetchJson, HttpError } from '../lib/api.ts';
import { useQuery } from '@tanstack/react-query';

export type User = {
  email: string;
  iamId: string | null;
  id: string;
  isEmulating?: boolean;
  isSiteAdmin: boolean;
  name: string;
  roles: string[];
};

export const meQueryOptions = () => ({
  queryFn: async (): Promise<User> => {
    return await fetchJson<User>('/api/user/me', { skipRedirectOn401: true });
  },
  queryKey: ['users', 'me'] as const,
  retry: (failureCount: number, error: Error) =>
    !(error instanceof HttpError && (error.status === 401 || error.status === 403)) &&
    failureCount < 3,
  staleTime: 5 * 60_000, // 5 minutes
});

export const useMeQuery = () => {
  return useQuery(meQueryOptions());
};
