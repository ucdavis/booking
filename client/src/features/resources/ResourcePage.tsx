import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { ResourceEditor } from './ResourceEditor.tsx';
import type { ResourceDetail } from './models/ResourceDetail.ts';
import { TeamRole } from '@/features/teams/models/TeamRole.ts';
import { HttpError } from '@/lib/api.ts';
import {
  resourceOptionsQueryOptions,
  resourceQueryOptions,
} from '@/queries/resources.ts';
import { teamAccessQueryOptions } from '@/queries/teams.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';

export function ResourcePage({
  resourceId,
  teamSlug,
}: {
  resourceId?: string;
  teamSlug: string;
}) {
  const navigate = useNavigate();
  const id = Number(resourceId);
  const isNew = resourceId === undefined;
  const validId =
    isNew ||
    (/^\d+$/.test(resourceId) &&
      Number.isSafeInteger(id) &&
      id > 0 &&
      id <= 2_147_483_647);
  const accessQuery = useQuery({
    ...teamAccessQueryOptions(teamSlug),
    refetchOnMount: false,
  });
  const access = accessQuery.data;
  const canEdit =
    accessQuery.isSuccess &&
    !!access &&
    (access.isSiteAdmin ||
      access.roles.includes(TeamRole.Admin) ||
      access.roles.includes(TeamRole.Editor));
  const optionsQuery = useQuery({
    ...resourceOptionsQueryOptions(teamSlug),
    enabled: canEdit && validId,
  });
  const resourceQuery = useQuery({
    ...resourceQueryOptions(teamSlug, id),
    enabled: canEdit && validId && !isNew,
    refetchOnMount: 'always',
    refetchOnReconnect: false,
    refetchOnWindowFocus: false,
  });
  const [openedResource, setOpenedResource] = useState<ResourceDetail>();
  if (
    !openedResource &&
    resourceQuery.isSuccess &&
    resourceQuery.isFetchedAfterMount
  ) {
    setOpenedResource(resourceQuery.data);
  }

  if (!accessQuery.isSuccess) {
    return null;
  }
  if (
    !canEdit ||
    [resourceQuery.error, optionsQuery.error].some(
      (error) => error instanceof HttpError && error.status === 403
    )
  ) {
    return (
      <NotAuthorizedPage description="You need Editor or administrator access to manage this team's resources." />
    );
  }
  const notFound =
    !validId ||
    (resourceQuery.error instanceof HttpError &&
      resourceQuery.error.status === 404);
  const loading =
    !notFound &&
    (optionsQuery.isPending ||
      (!isNew && !openedResource && !resourceQuery.isError));

  if (!notFound && optionsQuery.data && (isNew || openedResource)) {
    return (
      <>
        {(optionsQuery.isError || resourceQuery.isError) && (
          <p className="alert alert-warning mb-4" role="status">
            We could not refresh these settings. Your current draft is still
            available.
          </p>
        )}
        <ResourceEditor
          key={`${teamSlug}:${resourceId ?? 'new'}`}
          onSaved={async (resource) => {
            if (isNew) {
              await navigate({
                params: { resourceId: String(resource.id), teamSlug },
                replace: true,
                to: '/teams/$teamSlug/resources/$resourceId',
              });
            }
          }}
          options={optionsQuery.data}
          resource={openedResource}
          teamSlug={teamSlug}
        />
      </>
    );
  }

  return (
    <section>
      <Link
        className="link link-primary text-sm"
        params={{ teamSlug }}
        to="/teams/$teamSlug/resources"
      >
        Back to resources
      </Link>
      <h2 className="mt-4 text-2xl font-semibold text-primary">
        {notFound
          ? 'Resource not found'
          : isNew
            ? 'Create resource'
            : (openedResource?.name ?? 'Edit resource')}
      </h2>
      {notFound ? (
        <p className="mt-4">
          Check the resource URL or return to the resource list.
        </p>
      ) : loading ? (
        <p className="py-8" role="status">
          Loading resource…
        </p>
      ) : optionsQuery.isError || resourceQuery.isError ? (
        <p className="alert alert-error mt-6" role="alert">
          We could not load the resource settings. Return to the resource list
          and try again.
        </p>
      ) : null}
    </section>
  );
}
