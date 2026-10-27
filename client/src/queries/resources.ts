import { queryOptions } from '@tanstack/react-query';
import type { ResourceDetail } from '@/features/resources/models/ResourceDetail.ts';
import type { ResourceOptions } from '@/features/resources/models/ResourceOptions.ts';
import type { ResourceSummary } from '@/features/resources/models/ResourceSummary.ts';
import type { SaveResourceRequest } from '@/features/resources/models/SaveResourceRequest.ts';
import { fetchJson, HttpError } from '@/lib/api.ts';

const resourcesUrl = (teamSlug: string) =>
  `/api/teams/${encodeURIComponent(teamSlug)}/resources`;

export const resourcesKey = (teamSlug: string) =>
  ['teams', 'resources', teamSlug] as const;

const retry = (failureCount: number, error: Error) =>
  !(error instanceof HttpError && error.status >= 400 && error.status < 500) &&
  failureCount < 2;

export const resourcesQueryOptions = (teamSlug: string) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<ResourceSummary[]>(
        resourcesUrl(teamSlug),
        { cache: 'no-store' },
        signal
      ),
    queryKey: resourcesKey(teamSlug),
    retry,
  });

export const resourceQueryOptions = (teamSlug: string, id: number) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<ResourceDetail>(
        `${resourcesUrl(teamSlug)}/${id}`,
        { cache: 'no-store' },
        signal
      ),
    queryKey: [...resourcesKey(teamSlug), id] as const,
    retry,
  });

export const resourceOptionsQueryOptions = (teamSlug: string) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<ResourceOptions>(
        `${resourcesUrl(teamSlug)}/options`,
        { cache: 'no-store' },
        signal
      ),
    queryKey: [...resourcesKey(teamSlug), 'options'] as const,
    retry,
  });

export const createResource = (
  teamSlug: string,
  request: SaveResourceRequest
) =>
  fetchJson<ResourceDetail>(resourcesUrl(teamSlug), {
    body: JSON.stringify(request),
    method: 'POST',
  });

export const updateResource = (
  teamSlug: string,
  id: number,
  request: SaveResourceRequest
) =>
  fetchJson<ResourceDetail>(`${resourcesUrl(teamSlug)}/${id}`, {
    body: JSON.stringify(request),
    method: 'PUT',
  });

export function resourceErrorMessage(error: unknown): string {
  if (error instanceof HttpError) {
    if (error.status === 403) {
      return 'You no longer have permission to manage this team’s resources.';
    }
    if (error.status === 404) {
      return 'This resource or its selected template or location could not be found.';
    }
    if ([400, 409].includes(error.status)) {
      const body = error.body;
      const message =
        typeof body === 'string'
          ? body
          : body &&
              typeof body === 'object' &&
              'detail' in body &&
              typeof body.detail === 'string'
            ? body.detail
            : undefined;
      if (
        message &&
        message.length < 1000 &&
        !message.trimStart().startsWith('<')
      ) {
        return `${message} Your entered values are still here.`;
      }
      return error.status === 409
        ? 'This resource changed or conflicts with an existing resource. Your entered values are still here; reopen the resource before trying again.'
        : 'Check the resource details, location, form, opening hours, and rates. Your entered values are still here.';
    }
  }
  return 'We could not save your changes. Please try again.';
}
