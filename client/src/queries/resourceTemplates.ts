import { queryOptions } from '@tanstack/react-query';
import type { ResourceTemplate } from '@/features/resource-templates/models/ResourceTemplate.ts';
import type { SaveResourceTemplateRequest } from '@/features/resource-templates/models/SaveResourceTemplateRequest.ts';
import { fetchJson, HttpError } from '@/lib/api.ts';

const templatesUrl = '/api/admin/resource-templates';
export const resourceTemplatesQueryKey = ['admin', 'resource-templates'] as const;

const retry = (failureCount: number, error: Error) =>
  !(error instanceof HttpError && error.status >= 400 && error.status < 500) &&
  failureCount < 2;

export const resourceTemplatesQueryOptions = () =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<ResourceTemplate[]>(templatesUrl, { cache: 'no-store' }, signal),
    queryKey: resourceTemplatesQueryKey,
    retry,
  });

export const resourceTemplateQueryOptions = (id: number) =>
  queryOptions({
    queryFn: ({ signal }) =>
      fetchJson<ResourceTemplate>(`${templatesUrl}/${id}`, { cache: 'no-store' }, signal),
    queryKey: [...resourceTemplatesQueryKey, id] as const,
    retry,
  });

async function templateRequest(url: string, init: RequestInit) {
  const { token } = await fetchJson<{ token: string }>('/api/admin/antiforgery', {
    cache: 'no-store',
  });
  return fetchJson<ResourceTemplate>(url, {
    ...init,
    headers: { RequestVerificationToken: token },
  });
}

export const createResourceTemplate = (request: SaveResourceTemplateRequest) =>
  templateRequest(templatesUrl, { body: JSON.stringify(request), method: 'POST' });

export const updateResourceTemplate = (id: number, request: SaveResourceTemplateRequest) =>
  templateRequest(`${templatesUrl}/${id}`, { body: JSON.stringify(request), method: 'PUT' });

export const duplicateResourceTemplate = (id: number) =>
  templateRequest(`${templatesUrl}/${id}/duplicate`, { method: 'POST' });

export function resourceTemplateErrorMessage(error: unknown) {
  if (error instanceof HttpError) {
    if (error.status === 403) return 'You no longer have permission to manage resource templates.';
    if (error.status === 404) return 'This resource template could not be found.';
    if (error.status === 409) return 'This template changed since you opened it or was archived. Your edits are still here; reopen the current template from the resource template list before saving again.';
    if (error.status === 400) return 'Check the template name, fields, and validation rules, then try again.';
  }
  return 'We could not save your changes. Please try again.';
}
