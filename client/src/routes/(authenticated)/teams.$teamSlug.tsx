import { UserGroupIcon } from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import {
  createFileRoute,
  Link,
  Outlet,
  redirect,
  type ErrorComponentProps,
} from '@tanstack/react-router';
import { useEffect } from 'react';
import { HttpError } from '@/lib/api.ts';
import { teamAccessQueryOptions } from '@/queries/teams.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';

export const Route = createFileRoute('/(authenticated)/teams/$teamSlug')({
  beforeLoad: async ({ context, location, params }) => {
    try {
      const teamAccess = await context.queryClient.fetchQuery(
        teamAccessQueryOptions(params.teamSlug)
      );
      return { teamAccess };
    } catch (error) {
      if (error instanceof HttpError && error.status === 401) {
        throw redirect({
          href: `/login?returnUrl=${encodeURIComponent(location.href)}`,
          reloadDocument: true,
        });
      }
      throw error;
    }
  },
  component: TeamLayout,
  errorComponent: TeamRouteError,
  pendingComponent: () => (
    <main className="content-container py-12" role="status">
      Loading team…
    </main>
  ),
});

function TeamLayout() {
  const { teamSlug } = Route.useParams();
  const teamQuery = useQuery({
    ...teamAccessQueryOptions(teamSlug),
    refetchOnMount: false,
  });

  useEffect(() => {
    if (
      teamQuery.error instanceof HttpError &&
      teamQuery.error.status === 401
    ) {
      const returnUrl =
        window.location.pathname +
        window.location.search +
        window.location.hash;
      window.location.href = `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
    }
  }, [teamQuery.error]);

  if (teamQuery.error instanceof HttpError && teamQuery.error.status === 401) {
    return (
      <p className="content-container py-12" role="status">
        Redirecting to sign in…
      </p>
    );
  }

  if (teamQuery.isError) {
    return <TeamAccessError error={teamQuery.error} />;
  }

  if (teamQuery.isPending) {
    return (
      <p className="content-container py-12" role="status">
        Loading team…
      </p>
    );
  }

  const { isSiteAdmin, team } = teamQuery.data;

  return (
    <main className="content-container py-4 sm:py-8">
      <nav
        aria-label="Breadcrumb"
        className="mb-5 text-sm text-base-content/65"
      >
        <Link className="hover:text-primary hover:underline" to="/temp">
          Home
        </Link>{' '}
        <span aria-hidden="true">/</span>{' '}
        <span aria-current="page">{team.name}</span>
      </nav>

      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div className="min-w-0">
          <div className="flex items-center gap-3 text-primary">
            <UserGroupIcon aria-hidden="true" className="h-6 w-6" />
            <p className="text-sm font-semibold tracking-wide uppercase">
              Team administration
            </p>
          </div>
          <h1 className="mt-3 break-words text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
            {team.name}
          </h1>
          <p className="mt-3 text-base-content/70">
            View your team and its administration tools.
          </p>
        </div>
        {isSiteAdmin && (
          <Link className="btn btn-outline shrink-0" to="/admin/teams">
            All teams
          </Link>
        )}
      </div>

      <nav
        aria-label="Team administration"
        className="mt-8 flex gap-6 overflow-x-auto border-b border-base-300"
      >
        <Link
          activeOptions={{ exact: true }}
          activeProps={{ className: 'border-primary text-primary' }}
          className="border-b-2 px-1 py-3 text-sm font-semibold hover:text-primary"
          inactiveProps={{ className: 'border-transparent text-base-content/70' }}
          params={{ teamSlug: team.slug }}
          to="/teams/$teamSlug"
        >
          Overview
        </Link>
      </nav>

      <div className="py-8">
        <Outlet />
      </div>
    </main>
  );
}

function TeamRouteError({ error }: ErrorComponentProps<unknown>) {
  return <TeamAccessError error={error} />;
}

function TeamAccessError({ error }: { error: unknown }) {
  if (error instanceof HttpError && error.status === 403) {
    return (
      <NotAuthorizedPage description="You don't have permission to access this team." />
    );
  }

  const isNotFound = error instanceof HttpError && error.status === 404;
  return (
    <main className="content-container py-12 sm:py-16">
      <section className="max-w-2xl">
        <h1 className="text-3xl font-semibold text-primary">
          {isNotFound ? 'Team not found' : 'We could not load this team'}
        </h1>
        <p className="mt-4 text-base-content/70">
          {isNotFound
            ? 'Check the team URL and try again.'
            : 'Refresh the page or try again later.'}
        </p>
        <Link className="btn btn-primary mt-6" to="/temp">
          Back to Booking
        </Link>
      </section>
    </main>
  );
}
