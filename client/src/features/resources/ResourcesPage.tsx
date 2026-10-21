import { PlusIcon } from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { TeamRole } from '@/features/teams/models/TeamRole.ts';
import { HttpError } from '@/lib/api.ts';
import { resourcesQueryOptions } from '@/queries/resources.ts';
import { teamAccessQueryOptions } from '@/queries/teams.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';

export function ResourcesPage({ teamSlug }: { teamSlug: string }) {
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
  const resourcesQuery = useQuery({
    ...resourcesQueryOptions(teamSlug),
    enabled: canEdit,
  });
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('active');

  if (!accessQuery.isSuccess) {
    return null;
  }
  if (
    !canEdit ||
    (resourcesQuery.error instanceof HttpError &&
      resourcesQuery.error.status === 403)
  ) {
    return (
      <NotAuthorizedPage description="You need Editor or administrator access to manage this team's resources." />
    );
  }

  const resources = resourcesQuery.data ?? [];
  const filtered = resources.filter(
    (resource) =>
      (status === 'all' || resource.isActive === (status === 'active')) &&
      `${resource.name} ${resource.category} ${resource.spaceName}`
        .toLowerCase()
        .includes(search.trim().toLowerCase())
  );

  return (
    <section>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-semibold text-primary">Resources</h2>
          <p className="mt-2 max-w-2xl text-base-content/70">
            Manage your team’s rooms, equipment, and other resources. Start with
            a template, then customize each resource’s settings.
          </p>
        </div>
        <Link
          className="btn btn-primary"
          params={{ teamSlug }}
          to="/teams/$teamSlug/resources/new"
        >
          <PlusIcon aria-hidden="true" className="h-5 w-5" />
          Create resource
        </Link>
      </div>
      <div className="mt-6 flex flex-wrap gap-4">
        <label className="form-control flex min-w-56 flex-1 flex-col gap-1">
          <span className="text-sm font-medium">Search resources</span>
          <input
            className="input w-full"
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Name, type, or location"
            type="search"
            value={search}
          />
        </label>
        <label className="form-control flex flex-col gap-1">
          <span className="text-sm font-medium">Status</span>
          <select
            className="select"
            onChange={(event) => setStatus(event.target.value)}
            value={status}
          >
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
            <option value="all">All resources</option>
          </select>
        </label>
      </div>
      {resourcesQuery.isPending ? (
        <p className="py-8" role="status">
          Loading resources…
        </p>
      ) : resourcesQuery.isError ? (
        <div className="alert alert-error mt-6" role="alert">
          <span>We could not load these resources.</span>
          <button
            className="btn btn-sm"
            onClick={() => void resourcesQuery.refetch()}
            type="button"
          >
            Try again
          </button>
        </div>
      ) : resources.length === 0 ? (
        <div className="mt-6 rounded-sm border border-dashed border-base-300 p-8">
          <h3 className="text-lg font-semibold">No resources yet</h3>
          <p className="mt-2 text-base-content/70">
            Create your first resource from a team or system template. You can
            add its location as part of setup.
          </p>
        </div>
      ) : filtered.length === 0 ? (
        <p className="py-8" role="status">
          No resources match these filters.
        </p>
      ) : (
        <div className="mt-6 overflow-x-auto rounded-sm border border-base-300">
          <table className="table">
            <caption className="sr-only">Team resources</caption>
            <thead>
              <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Location</th>
                <th>Capacity</th>
                <th>Status</th>
                <th>
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((resource) => (
                <tr key={resource.id}>
                  <td className="font-medium">
                    <Link
                      className="link link-primary"
                      params={{ resourceId: String(resource.id), teamSlug }}
                      to="/teams/$teamSlug/resources/$resourceId"
                    >
                      {resource.name}
                    </Link>
                  </td>
                  <td>{resource.category}</td>
                  <td>{resource.spaceName}</td>
                  <td>{resource.capacity ?? '—'}</td>
                  <td>
                    <span
                      className={`badge badge-sm ${resource.isActive ? 'badge-success' : 'badge-ghost'}`}
                    >
                      {resource.isActive ? 'Active' : 'Inactive'}
                    </span>
                    {!resource.isReservable && (
                      <span className="ml-2 text-xs">Not reservable</span>
                    )}
                  </td>
                  <td>
                    <Link
                      aria-label={`Edit ${resource.name}`}
                      className="btn btn-ghost btn-sm"
                      params={{ resourceId: String(resource.id), teamSlug }}
                      to="/teams/$teamSlug/resources/$resourceId"
                    >
                      Edit
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
