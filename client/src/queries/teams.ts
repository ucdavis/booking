import { queryOptions } from '@tanstack/react-query';
import type { TeamAccess } from '@/features/teams/models/TeamAccess.ts';
import type { TeamSummary } from '@/features/teams/models/TeamSummary.ts';
import { fetchJson, HttpError } from '@/lib/api.ts';

export const myTeamsQueryOptions = (userId: string) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<TeamSummary[]>(
        '/api/teams',
        { cache: 'no-store', skipRedirectOn401: true },
        signal
      ),
    queryKey: ['teams', 'memberships', userId] as const,
    retry: (failureCount, error) =>
      !(
        error instanceof HttpError &&
        (error.status === 401 || error.status === 403)
      ) && failureCount < 2,
  });

export const teamAccessQueryOptions = (teamSlug: string) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<TeamAccess>(
        `/api/teams/${encodeURIComponent(teamSlug)}`,
        { cache: 'no-store', skipRedirectOn401: true },
        signal
      ),
    queryKey: ['teams', 'access', teamSlug] as const,
    retry: false,
    staleTime: 0,
  });
