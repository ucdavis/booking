import { queryOptions, type QueryClient } from '@tanstack/react-query';
import type { TeamAccess } from '@/features/teams/models/TeamAccess.ts';
import type { TeamMember } from '@/features/teams/models/TeamMember.ts';
import type { TeamPerson } from '@/features/teams/models/TeamPerson.ts';
import type { TeamRole } from '@/features/teams/models/TeamRole.ts';
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

const teamMembersUrl = (teamSlug: string) =>
  `/api/teams/${encodeURIComponent(teamSlug)}/members`;

export const teamMembersQueryKey = (teamSlug: string) =>
  ['teams', 'members', teamSlug] as const;

export const teamMembersQueryOptions = (teamSlug: string, userId: string) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<TeamMember[]>(
        teamMembersUrl(teamSlug),
        { cache: 'no-store', skipRedirectOn401: true },
        signal
      ),
    queryKey: [...teamMembersQueryKey(teamSlug), userId] as const,
    retry: false,
    staleTime: 0,
  });

export const teamPeopleQueryOptions = (
  teamSlug: string,
  userId: string,
  query: string | null
) =>
  queryOptions({
    enabled: !!query,
    gcTime: 0,
    queryFn: ({ signal }) =>
      fetchJson<TeamPerson[]>(
        `${teamMembersUrl(teamSlug)}/people?query=${encodeURIComponent(query ?? '')}`,
        { cache: 'no-store' },
        signal
      ),
    queryKey: ['teams', 'people', teamSlug, userId, query] as const,
    retry: false,
  });

export const addTeamMember = (
  teamSlug: string,
  iamId: string,
  role: TeamRole
) =>
  fetchJson<TeamMember>(teamMembersUrl(teamSlug), {
    body: JSON.stringify({ iamId, role }),
    method: 'POST',
  });

export const changeTeamMemberRole = (
  teamSlug: string,
  userId: number,
  role: TeamRole
) =>
  fetchJson<TeamMember>(`${teamMembersUrl(teamSlug)}/${userId}/role`, {
    body: JSON.stringify({ role }),
    method: 'PUT',
  });

export const removeTeamMember = (teamSlug: string, userId: number) =>
  fetchJson<void>(`${teamMembersUrl(teamSlug)}/${userId}`, { method: 'DELETE' });

export async function invalidateTeamMemberQueries(
  queryClient: QueryClient,
  teamSlug: string
) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: teamMembersQueryKey(teamSlug) }),
    queryClient.invalidateQueries({ queryKey: ['teams', 'people', teamSlug] }),
    queryClient.invalidateQueries({ queryKey: ['teams', 'access', teamSlug] }),
    queryClient.invalidateQueries({ queryKey: ['teams', 'memberships'] }),
  ]);
}
