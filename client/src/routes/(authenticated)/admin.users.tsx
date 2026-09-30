import { UserGroupIcon, UserPlusIcon } from '@heroicons/react/24/outline';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { ColumnDef } from '@tanstack/react-table';
import { useState } from 'react';
import { AddAdminUserDialog } from '@/features/admin/AddAdminUserDialog.tsx';
import type { AdminUser } from '@/features/admin/models/AdminUser.ts';
import { HttpError } from '@/lib/api.ts';
import {
  adminUsersQueryKey,
  adminUsersQueryOptions,
  removeAdminUser,
} from '@/queries/admin.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';
import { useUser } from '@/shared/auth/UserContext.tsx';
import { DataTable } from '@/shared/dataTable.tsx';

export const Route = createFileRoute('/(authenticated)/admin/users')({
  component: SiteAdminUsersPage,
});

function SiteAdminUsersPage() {
  const currentUser = useUser();
  const queryClient = useQueryClient();
  const usersQuery = useQuery(adminUsersQueryOptions());
  const [isAddOpen, setIsAddOpen] = useState(false);
  const [removingUser, setRemovingUser] = useState<AdminUser | null>(null);
  const [notice, setNotice] = useState('');
  const isCurrentUser = (user: AdminUser) =>
    user.iamId.trim().toLowerCase() === currentUser.iamId?.trim().toLowerCase();
  const removeMutation = useMutation({
    mutationFn: (user: AdminUser) => removeAdminUser(user.id),
    onSuccess: async (_data, user) => {
      await queryClient.invalidateQueries({ queryKey: adminUsersQueryKey });
      setRemovingUser(null);
      setNotice(`Admin access removed for ${user.name}.`);
    },
  });

  const columns: ColumnDef<AdminUser>[] = [
    {
      accessorKey: 'name',
      cell: ({ row }) => (
        <span className="font-medium">{row.original.name}</span>
      ),
      header: 'Name',
    },
    {
      accessorKey: 'email',
      cell: ({ row }) => row.original.email || 'Not available',
      header: 'Email',
    },
    { accessorKey: 'iamId', header: 'IAM ID' },
    {
      accessorKey: 'isActive',
      cell: ({ row }) => (
        <span
          className={`badge badge-sm ${row.original.isActive ? 'badge-success badge-outline' : 'badge-ghost'}`}
        >
          {row.original.isActive ? 'Active' : 'Inactive'}
        </span>
      ),
      header: 'Status',
    },
    {
      cell: ({ row }) =>
        isCurrentUser(row.original) ? (
          <span className="badge badge-outline">You</span>
        ) : (
          <button
            aria-label={`Remove admin access for ${row.original.name}`}
            className="btn btn-ghost btn-sm text-error"
            disabled={removeMutation.isPending}
            onClick={() => {
              setRemovingUser(row.original);
              setNotice('');
              removeMutation.reset();
            }}
            type="button"
          >
            Remove
          </button>
        ),
      enableSorting: false,
      header: 'Actions',
      id: 'actions',
    },
  ];

  if (
    usersQuery.error instanceof HttpError &&
    usersQuery.error.status === 403
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
        <span aria-current="page">Admin users</span>
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
            Site admin users
          </h1>
          <p className="mt-3 max-w-2xl text-base-content/70">
            Manage who can administer Booking. You can add a person or remove
            another user&apos;s admin access.
          </p>
        </div>
        <button
          className="btn btn-primary shrink-0"
          onClick={() => {
            setNotice('');
            setIsAddOpen(true);
          }}
          type="button"
        >
          <UserPlusIcon aria-hidden="true" className="h-5 w-5" />
          Add admin user
        </button>
      </div>

      {notice && (
        <p className="alert alert-success mt-6" role="status">
          {notice}
        </p>
      )}

      {removingUser && (
        <section
          aria-labelledby="remove-admin-heading"
          className="mt-6 rounded-xl border border-warning/40 bg-warning/10 p-5"
        >
          <h2 className="font-semibold" id="remove-admin-heading">
            Remove admin access?
          </h2>
          <p className="mt-2 text-base-content/80">
            {removingUser.name} will no longer be a site admin. Their user
            record and other access will remain.
          </p>
          {removeMutation.isError && (
            <p className="mt-3 text-error" role="alert">
              {removeMutation.error instanceof HttpError &&
              removeMutation.error.status === 403
                ? 'You no longer have permission to manage site admin users.'
                : 'We could not remove admin access. Please try again.'}
            </p>
          )}
          <div className="mt-4 flex flex-wrap gap-3">
            <button
              className="btn btn-error btn-sm"
              disabled={removeMutation.isPending}
              onClick={() => {
                if (!isCurrentUser(removingUser)) {
                  removeMutation.mutate(removingUser);
                }
              }}
              type="button"
            >
              {removeMutation.isPending ? 'Removing…' : 'Confirm removal'}
            </button>
            <button
              className="btn btn-ghost btn-sm"
              disabled={removeMutation.isPending}
              onClick={() => setRemovingUser(null)}
              type="button"
            >
              Cancel
            </button>
          </div>
        </section>
      )}

      <section
        aria-label="Site admin users"
        className="mt-8 rounded-xl border border-base-300 bg-base-100 p-4 shadow-sm sm:p-6"
      >
        {usersQuery.isPending ? (
          <p className="py-8 text-center text-base-content/70" role="status">
            Loading admin users…
          </p>
        ) : usersQuery.isError ? (
          <div className="py-6 text-center">
            <p role="alert">
              We could not load the admin users. Please try again.
            </p>
            <button
              className="btn btn-outline mt-4"
              onClick={() => void usersQuery.refetch()}
              type="button"
            >
              Try again
            </button>
          </div>
        ) : usersQuery.data.length === 0 ? (
          <p className="py-8 text-center text-base-content/70">
            There are no site admin users.
          </p>
        ) : (
          <DataTable
            columns={columns}
            data={usersQuery.data}
            filterPlaceholder="Search admin users…"
            initialState={{
              pagination: { pageSize: 10 },
              sorting: [{ desc: false, id: 'name' }],
            }}
            tableActions={
              <p className="text-sm text-base-content/60">
                {usersQuery.data.length} admin{' '}
                {usersQuery.data.length === 1 ? 'user' : 'users'}
              </p>
            }
          />
        )}
      </section>

      {isAddOpen && (
        <AddAdminUserDialog
          onAdded={(user) => {
            setIsAddOpen(false);
            setNotice(`${user.name} is now a site admin user.`);
          }}
          onClose={() => setIsAddOpen(false)}
        />
      )}
    </main>
  );
}
