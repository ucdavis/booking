import { PlusIcon, UserGroupIcon } from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { ColumnDef } from '@tanstack/react-table';
import { useState } from 'react';
import { CreateTeamDialog } from '@/features/teams/CreateTeamDialog.tsx';
import type { TeamSummary } from '@/features/teams/models/TeamSummary.ts';
import { HttpError } from '@/lib/api.ts';
import { adminTeamsQueryOptions } from '@/queries/admin.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';
import { DataTable } from '@/shared/dataTable.tsx';

export const Route = createFileRoute('/(authenticated)/admin/teams')({
  component: SiteAdminTeamsPage,
});

const columns: ColumnDef<TeamSummary>[] = [
  {
    accessorKey: 'name',
    cell: ({ row }) => (
      <Link
        className="font-semibold text-primary hover:underline"
        params={{ teamSlug: row.original.slug }}
        to="/teams/$teamSlug"
      >
        {row.original.name}
      </Link>
    ),
    header: 'Team name',
  },
  {
    accessorKey: 'slug',
    cell: ({ row }) => (
      <span className="break-all text-base-content/70">
        /teams/{row.original.slug}
      </span>
    ),
    header: 'Team URL',
  },
];

function SiteAdminTeamsPage() {
  const teamsQuery = useQuery(adminTeamsQueryOptions());
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const navigate = Route.useNavigate();

  if (
    teamsQuery.error instanceof HttpError &&
    teamsQuery.error.status === 403
  ) {
    return <NotAuthorizedPage />;
  }

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
        <Link className="hover:text-primary hover:underline" to="/admin">
          Site admin
        </Link>{' '}
        <span aria-hidden="true">/</span> <span aria-current="page">Teams</span>
      </nav>

      <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <div className="flex items-center gap-3 text-primary">
            <UserGroupIcon aria-hidden="true" className="h-6 w-6" />
            <p className="text-sm font-semibold tracking-wide uppercase">
              Booking administration
            </p>
          </div>
          <h1 className="mt-3 text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
            Teams
          </h1>
          <p className="mt-3 max-w-2xl text-base-content/70">
            View all teams in Booking, open a team&apos;s administration, or
            create a new team.
          </p>
        </div>
        <button
          className="btn btn-primary shrink-0"
          onClick={() => setIsCreateOpen(true)}
          type="button"
        >
          <PlusIcon aria-hidden="true" className="h-5 w-5" />
          Create team
        </button>
      </div>

      <section
        aria-label="All teams"
        className="mt-8 rounded-xl border border-base-300 bg-base-100 p-4 shadow-sm sm:p-6"
      >
        {teamsQuery.isPending ? (
          <p className="py-8 text-center text-base-content/70" role="status">
            Loading teams…
          </p>
        ) : teamsQuery.isError ? (
          <div className="py-6 text-center">
            <p role="alert">We could not load the teams. Please try again.</p>
            <button
              className="btn btn-outline mt-4"
              onClick={() => void teamsQuery.refetch()}
              type="button"
            >
              Try again
            </button>
          </div>
        ) : teamsQuery.data.length === 0 ? (
          <p className="py-8 text-center text-base-content/70">
            No teams have been created yet. Create a team to get started.
          </p>
        ) : (
          <DataTable
            columns={columns}
            data={teamsQuery.data}
            filterPlaceholder="Search teams…"
            initialState={{
              pagination: { pageSize: 10 },
              sorting: [{ desc: false, id: 'name' }],
            }}
            tableActions={
              <p className="text-sm text-base-content/60">
                {teamsQuery.data.length}{' '}
                {teamsQuery.data.length === 1 ? 'team' : 'teams'}
              </p>
            }
          />
        )}
      </section>

      {isCreateOpen && (
        <CreateTeamDialog
          onClose={() => setIsCreateOpen(false)}
          onCreated={async (team) => {
            setIsCreateOpen(false);
            await navigate({
              params: { teamSlug: team.slug },
              to: '/teams/$teamSlug',
            });
          }}
        />
      )}
    </main>
  );
}
