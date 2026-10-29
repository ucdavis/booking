import { queryOptions } from '@tanstack/react-query';
import type { SaveSpaceRequest } from '@/features/spaces/models/SaveSpaceRequest.ts';
import type { Space } from '@/features/spaces/models/Space.ts';
import type { SpaceTeamAccess } from '@/features/spaces/models/SpaceTeamAccess.ts';
import { fetchJson, HttpError } from '@/lib/api.ts';

export const adminSpacesQueryKey = ['admin', 'spaces'] as const;

export const adminSpacesQueryOptions = () =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<Space[]>('/api/admin/spaces', { cache: 'no-store' }, signal),
    queryKey: adminSpacesQueryKey,
    retry: (failureCount, error) =>
      !(
        error instanceof HttpError &&
        (error.status === 401 || error.status === 403)
      ) && failureCount < 2,
  });

export const createSpace = (request: SaveSpaceRequest) =>
  fetchJson<Space>('/api/admin/spaces', {
    body: JSON.stringify(request),
    method: 'POST',
  });

export const updateSpace = (id: number, request: SaveSpaceRequest) =>
  fetchJson<Space>(`/api/admin/spaces/${id}`, {
    body: JSON.stringify(request),
    method: 'PUT',
  });

export const spaceTeamsQueryOptions = (spaceId: number) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<SpaceTeamAccess[]>(
        `/api/admin/spaces/${spaceId}/teams`,
        { cache: 'no-store' },
        signal
      ),
    queryKey: [...adminSpacesQueryKey, spaceId, 'teams'] as const,
    retry: (failureCount, error) =>
      !(
        error instanceof HttpError &&
        error.status >= 400 &&
        error.status < 500
      ) && failureCount < 2,
  });

export const assignSpaceTeam = (spaceId: number, teamId: number) =>
  fetchJson<SpaceTeamAccess>(`/api/admin/spaces/${spaceId}/teams/${teamId}`, {
    method: 'PUT',
  });

export const unlinkSpaceTeam = (
  spaceId: number,
  teamId: number,
  confirmedResourceCount?: number
) =>
  fetchJson<void>(
    `/api/admin/spaces/${spaceId}/teams/${teamId}${
      confirmedResourceCount === undefined
        ? ''
        : `?confirmedResourceCount=${confirmedResourceCount}`
    }`,
    { method: 'DELETE' }
  );

export function confirmationResourceCount(error: unknown): number | undefined {
  if (!(error instanceof HttpError) || error.status !== 409) {
    return undefined;
  }
  let details = error.body;
  if (typeof details === 'string') {
    try {
      details = JSON.parse(details);
    } catch {
      return undefined;
    }
  }
  if (
    details !== null &&
    typeof details === 'object' &&
    'code' in details &&
    details.code === 'space_team_resources_confirmation_required' &&
    'resourceCount' in details &&
    typeof details.resourceCount === 'number' &&
    Number.isSafeInteger(details.resourceCount) &&
    details.resourceCount >= 0
  ) {
    return details.resourceCount;
  }
  return undefined;
}
