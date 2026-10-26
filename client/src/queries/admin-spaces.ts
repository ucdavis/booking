import { queryOptions } from '@tanstack/react-query';
import type { SaveSpaceRequest } from '@/features/spaces/models/SaveSpaceRequest.ts';
import type { Space } from '@/features/spaces/models/Space.ts';
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
