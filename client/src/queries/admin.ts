import { queryOptions } from '@tanstack/react-query';
import type { AdminPerson } from '@/features/admin/models/AdminPerson.ts';
import type { AdminUser } from '@/features/admin/models/AdminUser.ts';
import type { CreateTeamRequest } from '@/features/teams/models/CreateTeamRequest.ts';
import type { TeamSummary } from '@/features/teams/models/TeamSummary.ts';
import { fetchJson, HttpError } from '@/lib/api.ts';

export const adminUsersQueryKey = ['admin', 'users'] as const;
export const adminTeamsQueryKey = ['admin', 'teams'] as const;

export const adminTeamsQueryOptions = () =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<TeamSummary[]>('/api/admin/teams', { cache: 'no-store' }, signal),
    queryKey: adminTeamsQueryKey,
    retry: (failureCount, error) =>
      !(
        error instanceof HttpError &&
        (error.status === 401 || error.status === 403)
      ) && failureCount < 2,
  });

export const adminUsersQueryOptions = () =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<AdminUser[]>('/api/admin/users', { cache: 'no-store' }, signal),
    queryKey: adminUsersQueryKey,
    retry: (failureCount, error) =>
      !(
        error instanceof HttpError &&
        (error.status === 401 || error.status === 403)
      ) && failureCount < 2,
  });

export const adminPeopleQueryOptions = (query: string | null) =>
  queryOptions({
    enabled: !!query,
    gcTime: 0,
    queryFn: ({ signal }) =>
      fetchJson<AdminPerson[]>(
        `/api/admin/people?query=${encodeURIComponent(query ?? '')}`,
        { cache: 'no-store' },
        signal
      ),
    queryKey: ['admin', 'people', query] as const,
    retry: false,
  });

async function adminRequest<T>(url: string, init: RequestInit): Promise<T> {
  const { token } = await fetchJson<{ token: string }>(
    '/api/admin/antiforgery',
    {
      cache: 'no-store',
    }
  );

  return fetchJson<T>(url, {
    ...init,
    headers: { RequestVerificationToken: token },
  });
}

export const addAdminUser = (iamId: string) =>
  adminRequest<AdminUser>('/api/admin/users', {
    body: JSON.stringify({ iamId }),
    method: 'POST',
  });

export const removeAdminUser = (id: number) =>
  adminRequest<void>(`/api/admin/users/${id}`, { method: 'DELETE' });

export const createTeam = (request: CreateTeamRequest) =>
  adminRequest<TeamSummary>('/api/admin/teams', {
    body: JSON.stringify(request),
    method: 'POST',
  });
