import { PlusIcon } from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { ColumnDef } from '@tanstack/react-table';
import { useCallback, useMemo, useState } from 'react';
import { SpaceEditorDialog } from '@/features/spaces/SpaceEditorDialog.tsx';
import { SpaceTeamsDialog } from '@/features/spaces/SpaceTeamsDialog.tsx';
import type { Space } from '@/features/spaces/models/Space.ts';
import { HttpError } from '@/lib/api.ts';
import { adminSpacesQueryOptions } from '@/queries/admin-spaces.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';
import { DataTable } from '@/shared/dataTable.tsx';

export const Route = createFileRoute('/(authenticated)/admin/spaces')({
  component: SiteAdminSpacesPage,
});

function SiteAdminSpacesPage() {
  const spacesQuery = useQuery(adminSpacesQueryOptions());
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [editingSpace, setEditingSpace] = useState<Space | null>(null);
  const [managingTeamsSpace, setManagingTeamsSpace] = useState<Space | null>(
    null
  );
  const [notice, setNotice] = useState('');
  const openEditor = useCallback((space: Space | null) => {
    setEditingSpace(space);
    setNotice('');
    setIsEditorOpen(true);
  }, []);
  const columns = useMemo<ColumnDef<Space>[]>(
    () => [
      {
        accessorKey: 'name',
        cell: ({ row }) => (
          <span className="font-medium">{row.original.name}</span>
        ),
        header: 'Space name',
      },
      {
        accessorKey: 'slug',
        cell: ({ row }) => (
          <span className="break-all text-base-content/70">
            {row.original.slug}
          </span>
        ),
        header: 'URL slug',
      },
      {
        cell: ({ row }) => (
          <div className="flex flex-wrap gap-2">
            <button
              aria-label={`Edit ${row.original.name}`}
              className="btn btn-ghost btn-sm text-primary"
              onClick={() => openEditor(row.original)}
              type="button"
            >
              Edit
            </button>
            <button
              aria-label={`Manage teams for ${row.original.name}`}
              className="btn btn-ghost btn-sm text-primary"
              onClick={() => setManagingTeamsSpace(row.original)}
              type="button"
            >
              Manage teams
            </button>
          </div>
        ),
        enableSorting: false,
        header: 'Actions',
        id: 'actions',
      },
    ],
    [openEditor]
  );

  if (
    spacesQuery.error instanceof HttpError &&
    spacesQuery.error.status === 403
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
        <span aria-hidden="true">/</span>{' '}
        <span aria-current="page">Spaces</span>
      </nav>

      <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
            Spaces
          </h1>
          <p className="mt-3 max-w-2xl text-base-content/70">
            Create and update buildings and other named locations in Booking.
          </p>
        </div>
        <button
          className="btn btn-primary shrink-0"
          onClick={() => openEditor(null)}
          type="button"
        >
          <PlusIcon aria-hidden="true" className="h-5 w-5" />
          Create space
        </button>
      </div>

      {notice && (
        <p className="alert alert-success mt-6" role="status">
          {notice}
        </p>
      )}

      <section
        aria-label="All spaces"
        className="mt-8 rounded-sm border border-base-300 bg-base-200 p-4 shadow-sm sm:p-6"
      >
        {spacesQuery.isPending ? (
          <p className="py-8 text-center text-base-content/70" role="status">
            Loading spaces…
          </p>
        ) : spacesQuery.isError ? (
          <div className="py-6 text-center">
            <p role="alert">We could not load the spaces. Please try again.</p>
            <button
              className="btn btn-outline mt-4"
              onClick={() => void spacesQuery.refetch()}
              type="button"
            >
              Try again
            </button>
          </div>
        ) : spacesQuery.data.length === 0 ? (
          <p className="py-8 text-center text-base-content/70">
            No spaces have been created yet. Create a space to get started.
          </p>
        ) : (
          <DataTable
            columns={columns}
            data={spacesQuery.data}
            filterPlaceholder="Search spaces…"
            initialState={{
              pagination: { pageSize: 10 },
              sorting: [{ desc: false, id: 'name' }],
            }}
            tableActions={
              <p className="text-sm text-base-content/60">
                {spacesQuery.data.length}{' '}
                {spacesQuery.data.length === 1 ? 'space' : 'spaces'}
              </p>
            }
          />
        )}
      </section>

      {isEditorOpen && (
        <SpaceEditorDialog
          key={editingSpace?.id ?? 'new'}
          onClose={() => setIsEditorOpen(false)}
          onSaved={(space) => {
            setIsEditorOpen(false);
            setNotice(`${space.name} ${editingSpace ? 'updated' : 'created'}.`);
          }}
          space={editingSpace}
        />
      )}
      {managingTeamsSpace && (
        <SpaceTeamsDialog
          key={managingTeamsSpace.id}
          onClose={() => setManagingTeamsSpace(null)}
          space={managingTeamsSpace}
        />
      )}
    </main>
  );
}
