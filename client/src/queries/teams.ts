import {
  queryOptions,
  type MutationFilters,
  type QueryClient,
} from '@tanstack/react-query';
import type { Space } from '@/features/spaces/models/Space.ts';
import type { TeamAccess } from '@/features/teams/models/TeamAccess.ts';
import type { TeamMember } from '@/features/teams/models/TeamMember.ts';
import type { TeamPaymentsSettings } from '@/features/teams/models/TeamPaymentsSettings.ts';
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

const teamPaymentsUrl = (teamSlug: string) =>
  `/api/teams/${encodeURIComponent(teamSlug)}/payments`;

export const teamPaymentsQueryOptions = (teamSlug: string, userId: string) =>
  queryOptions({
    gcTime: 0,
    queryFn: ({ signal }) =>
      fetchJson<TeamPaymentsSettings>(
        teamPaymentsUrl(teamSlug),
        { cache: 'no-store' },
        signal
      ),
    queryKey: ['teams', 'payments', teamSlug, userId] as const,
    retry: false,
    staleTime: 0,
  });

export async function saveTeamPaymentsSettings(
  teamSlug: string,
  apiKey: string,
  signal?: AbortSignal
) {
  try {
    return await fetchJson<TeamPaymentsSettings>(
      teamPaymentsUrl(teamSlug),
      { body: JSON.stringify({ apiKey }), method: 'PUT' },
      signal
    );
  } catch (error) {
    // Do not retain response bodies from a credential submission in caches.
    if (error instanceof HttpError) {
      throw new HttpError(error.status, error.url);
    }
    throw error;
  }
}

const teamMembersUrl = (teamSlug: string) =>
  `/api/teams/${encodeURIComponent(teamSlug)}/members`;

export const teamMembersQueryKey = (teamSlug: string) =>
  ['teams', 'members', teamSlug] as const;

export const teamMemberMutationKey = (teamSlug: string) =>
  ['teams', 'member-change', teamSlug] as const;

export const teamSelfAccessMutationFilter = (
  teamSlug: string
): MutationFilters => ({
  mutationKey: teamMemberMutationKey(teamSlug),
  predicate: (mutation) => {
    const variables = mutation.state.variables;
    return (
      typeof variables === 'object' &&
      variables !== null &&
      'changesOwnAccess' in variables &&
      variables.changesOwnAccess === true
    );
  },
});

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
  roles: TeamRole[]
) =>
  fetchJson<TeamMember>(teamMembersUrl(teamSlug), {
    body: JSON.stringify({ iamId, roles }),
    method: 'POST',
  });

export const changeTeamMemberRoles = (
  teamSlug: string,
  userId: number,
  roles: TeamRole[],
  confirmSelfAccessChange = false
) =>
  fetchJson<TeamMember>(`${teamMembersUrl(teamSlug)}/${userId}/roles`, {
    body: JSON.stringify({
      roles,
      ...(confirmSelfAccessChange ? { confirmSelfAccessChange: true } : {}),
    }),
    method: 'PUT',
  });

export const removeTeamMember = (
  teamSlug: string,
  userId: number,
  confirmSelfAccessChange = false
) =>
  fetchJson<void>(
    `${teamMembersUrl(teamSlug)}/${userId}${confirmSelfAccessChange ? '?confirmSelfAccessChange=true' : ''}`,
    {
      method: 'DELETE',
    }
  );

export async function invalidateTeamMemberQueries(
  queryClient: QueryClient,
  teamSlug: string,
  refetchType: 'active' | 'none' = 'active'
) {
  await queryClient.cancelQueries({ queryKey: ['teams', 'memberships'] });
  await Promise.all([
    queryClient.invalidateQueries({
      queryKey: teamMembersQueryKey(teamSlug),
      refetchType,
    }),
    queryClient.invalidateQueries({
      queryKey: ['teams', 'people', teamSlug],
      refetchType,
    }),
    queryClient.invalidateQueries({
      queryKey: ['teams', 'access', teamSlug],
      refetchType,
    }),
    queryClient.invalidateQueries({
      queryKey: ['teams', 'memberships'],
      refetchType,
    }),
  ]);
}

export const teamSpacesQueryOptions = (teamSlug: string) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<Space[]>(
        `/api/teams/${encodeURIComponent(teamSlug)}/spaces`,
        { cache: 'no-store' },
        signal
      ),
    queryKey: ['teams', 'spaces', teamSlug] as const,
    retry: false,
  });
