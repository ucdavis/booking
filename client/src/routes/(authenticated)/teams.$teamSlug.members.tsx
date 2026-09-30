import { UserPlusIcon } from '@heroicons/react/24/outline';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createFileRoute,
  Link,
  redirect,
  type ErrorComponentProps,
} from '@tanstack/react-router';
import type { ColumnDef } from '@tanstack/react-table';
import { useCallback, useEffect, useState } from 'react';
import { AddTeamMemberDialog } from '@/features/teams/AddTeamMemberDialog.tsx';
import type { TeamMember } from '@/features/teams/models/TeamMember.ts';
import {
  assignableTeamRoles,
  TeamRole,
  teamRoleLabels,
} from '@/features/teams/models/TeamRole.ts';
import { HttpError } from '@/lib/api.ts';
import {
  changeTeamMemberRole,
  invalidateTeamMemberQueries,
  removeTeamMember,
  teamAccessQueryOptions,
  teamMembersQueryOptions,
} from '@/queries/teams.ts';
import { meQueryOptions } from '@/queries/user.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';
import { DataTable } from '@/shared/dataTable.tsx';

export const Route = createFileRoute(
  '/(authenticated)/teams/$teamSlug/members'
)({
  beforeLoad: async ({ context, location, params }) => {
    try {
      const user = await context.queryClient.ensureQueryData(meQueryOptions());
      await context.queryClient.fetchQuery(
        teamMembersQueryOptions(params.teamSlug, user.id)
      );
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
  component: TeamMembersPage,
  errorComponent: ({ error }: ErrorComponentProps<unknown>) => (
    <TeamMembersError error={error} />
  ),
  pendingComponent: () => <p role="status">Loading team members…</p>,
  remountDeps: ({ params }) => params.teamSlug,
});

function TeamMembersPage() {
  const { teamSlug } = Route.useParams();
  const currentUser = useUser();
  const queryClient = useQueryClient();
  const accessQuery = useQuery({
    ...teamAccessQueryOptions(teamSlug),
    refetchOnMount: false,
  });
  const membersQuery = useQuery({
    ...teamMembersQueryOptions(teamSlug, currentUser.id),
    refetchOnMount: false,
  });
  const [isAddOpen, setIsAddOpen] = useState(false);
  const [editingMember, setEditingMember] = useState<TeamMember | null>(null);
  const [newRole, setNewRole] = useState<TeamRole>(TeamRole.Editor);
  const [removingMember, setRemovingMember] = useState<TeamMember | null>(null);
  const [notice, setNotice] = useState('');
  const [accessDenied, setAccessDenied] = useState(false);
  const denyAccess = useCallback(() => {
    setAccessDenied(true);
    void queryClient.invalidateQueries({
      queryKey: ['teams', 'access', teamSlug],
    });
  }, [queryClient, teamSlug]);
  const isCurrentUser = (member: TeamMember) =>
    member.iamId.trim().toLowerCase() ===
    currentUser.iamId?.trim().toLowerCase();
  const roleMutation = useMutation({
    mutationFn: ({ member, role }: { member: TeamMember; role: TeamRole }) =>
      changeTeamMemberRole(teamSlug, member.id, role),
    onError: (error) => {
      if (error instanceof HttpError && error.status === 403) {
        denyAccess();
      }
    },
    onSuccess: async (member) => {
      await invalidateTeamMemberQueries(queryClient, teamSlug);
      setEditingMember(null);
      setNotice(`${member.name}'s role is now ${teamRoleLabels[member.role]}.`);
    },
  });
  const removeMutation = useMutation({
    mutationFn: (member: TeamMember) => removeTeamMember(teamSlug, member.id),
    onError: (error) => {
      if (error instanceof HttpError && error.status === 403) {
        denyAccess();
      }
    },
    onSuccess: async (_data, member) => {
      await invalidateTeamMemberQueries(queryClient, teamSlug);
      setRemovingMember(null);
      setNotice(`${member.name} was removed from this team.`);
    },
  });

  useEffect(() => {
    if (!(membersQuery.error instanceof HttpError)) {
      return;
    }
    if (membersQuery.error.status === 401) {
      const returnUrl =
        window.location.pathname +
        window.location.search +
        window.location.hash;
      window.location.href = `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
    } else if (membersQuery.error.status === 403) {
      void queryClient.invalidateQueries({
        queryKey: ['teams', 'access', teamSlug],
      });
    }
  }, [membersQuery.error, queryClient, teamSlug]);

  const isBusy = roleMutation.isPending || removeMutation.isPending;
  const columns: ColumnDef<TeamMember>[] = [
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
      accessorKey: 'role',
      cell: ({ row }) => teamRoleLabels[row.original.role],
      header: 'Role',
    },
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
          <div className="flex flex-wrap gap-1">
            <button
              aria-label={`Change role for ${row.original.name}`}
              className="btn btn-ghost btn-sm"
              disabled={isBusy}
              onClick={() => {
                setEditingMember(row.original);
                setNewRole(
                  row.original.role === TeamRole.Admin
                    ? TeamRole.Admin
                    : TeamRole.Editor
                );
                setRemovingMember(null);
                setNotice('');
                roleMutation.reset();
              }}
              type="button"
            >
              Change role
            </button>
            <button
              aria-label={`Remove ${row.original.name} from team`}
              className="btn btn-ghost btn-sm text-error"
              disabled={isBusy}
              onClick={() => {
                setRemovingMember(row.original);
                setEditingMember(null);
                setNotice('');
                removeMutation.reset();
              }}
              type="button"
            >
              Remove
            </button>
          </div>
        ),
      enableSorting: false,
      header: 'Actions',
      id: 'actions',
    },
  ];

  if (
    membersQuery.error instanceof HttpError &&
    membersQuery.error.status === 401
  ) {
    return <p role="status">Redirecting to sign in…</p>;
  }

  const canManage =
    accessQuery.isSuccess &&
    (accessQuery.data.isSiteAdmin || accessQuery.data.role === TeamRole.Admin);
  if (accessDenied || !canManage) {
    return <TeamMembersError error={new HttpError(403, '/api/teams')} />;
  }
  if (membersQuery.isError) {
    return <TeamMembersError error={membersQuery.error} />;
  }
  if (membersQuery.isPending) {
    return <p role="status">Loading team members…</p>;
  }

  return (
    <section>
      <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="text-2xl font-semibold text-primary">Team members</h2>
          <p className="mt-3 max-w-2xl text-base-content/70">
            Add people and manage their roles on this team. You cannot change
            your own role or remove yourself.
          </p>
        </div>
        <button
          className="btn btn-primary shrink-0"
          disabled={isBusy}
          onClick={() => {
            setNotice('');
            setIsAddOpen(true);
          }}
          type="button"
        >
          <UserPlusIcon aria-hidden="true" className="h-5 w-5" />
          Add member
        </button>
      </div>

      {notice && (
        <p className="alert alert-success mt-6" role="status">
          {notice}
        </p>
      )}

      {editingMember && !isCurrentUser(editingMember) && (
        <section
          aria-labelledby="change-team-role-heading"
          className="mt-6 rounded-xl border border-base-300 bg-base-100 p-5"
        >
          <h3 className="font-semibold" id="change-team-role-heading">
            Change team role
          </h3>
          <p className="mt-2 text-base-content/80">
            Choose a new role for {editingMember.name}. Their current role is{' '}
            {teamRoleLabels[editingMember.role]}.
          </p>
          <label
            className="mt-4 mb-2 block text-sm font-semibold"
            htmlFor="team-member-new-role"
          >
            New role
          </label>
          <select
            className="select select-bordered w-full max-w-xs"
            disabled={roleMutation.isPending}
            id="team-member-new-role"
            onChange={(event) => {
              const selectedRole = assignableTeamRoles.find(
                (value) => value === event.target.value
              );
              if (selectedRole) {
                setNewRole(selectedRole);
                roleMutation.reset();
              }
            }}
            value={newRole}
          >
            {assignableTeamRoles.map((value) => (
              <option key={value} value={value}>
                {teamRoleLabels[value]}
              </option>
            ))}
          </select>
          <p className="mt-2 text-sm text-base-content/65">
            {newRole === TeamRole.Admin
              ? 'Admins can manage this team and its members.'
              : 'Editors have team access but cannot manage members.'}
          </p>
          {roleMutation.isError && (
            <p className="mt-3 text-error" role="alert">
              We could not change this member&apos;s role. Refresh the page to
              check their current access, then try again.
            </p>
          )}
          <div className="mt-4 flex flex-wrap gap-3">
            <button
              className="btn btn-primary btn-sm"
              disabled={
                roleMutation.isPending || newRole === editingMember.role
              }
              onClick={() => {
                if (!isCurrentUser(editingMember)) {
                  roleMutation.mutate({ member: editingMember, role: newRole });
                }
              }}
              type="button"
            >
              {roleMutation.isPending ? 'Saving…' : 'Save role'}
            </button>
            <button
              className="btn btn-ghost btn-sm"
              disabled={roleMutation.isPending}
              onClick={() => setEditingMember(null)}
              type="button"
            >
              Cancel
            </button>
          </div>
        </section>
      )}

      {removingMember && !isCurrentUser(removingMember) && (
        <section
          aria-labelledby="remove-team-member-heading"
          className="mt-6 rounded-xl border border-warning/40 bg-warning/10 p-5"
        >
          <h3 className="font-semibold" id="remove-team-member-heading">
            Remove team member?
          </h3>
          <p className="mt-2 text-base-content/80">
            Remove {removingMember.name}&apos;s role on this team. Their user
            account, other teams, and any site admin access will remain.
          </p>
          {removeMutation.isError && (
            <p className="mt-3 text-error" role="alert">
              We could not remove this team member. Refresh the page to check
              their current access, then try again.
            </p>
          )}
          <div className="mt-4 flex flex-wrap gap-3">
            <button
              className="btn btn-error btn-sm"
              disabled={removeMutation.isPending}
              onClick={() => {
                if (!isCurrentUser(removingMember)) {
                  removeMutation.mutate(removingMember);
                }
              }}
              type="button"
            >
              {removeMutation.isPending ? 'Removing…' : 'Confirm removal'}
            </button>
            <button
              className="btn btn-ghost btn-sm"
              disabled={removeMutation.isPending}
              onClick={() => setRemovingMember(null)}
              type="button"
            >
              Cancel
            </button>
          </div>
        </section>
      )}

      <section
        aria-label="Team members"
        className="mt-8 rounded-xl border border-base-300 bg-base-100 p-4 shadow-sm sm:p-6"
      >
        {membersQuery.data.length === 0 ? (
          <p className="py-8 text-center text-base-content/70">
            This team does not have any members yet.
          </p>
        ) : (
          <DataTable
            columns={columns}
            data={membersQuery.data}
            filterPlaceholder="Search team members…"
            initialState={{
              pagination: { pageSize: 10 },
              sorting: [{ desc: false, id: 'name' }],
            }}
            tableActions={
              <p className="text-sm text-base-content/60">
                {membersQuery.data.length}{' '}
                {membersQuery.data.length === 1 ? 'member' : 'members'}
              </p>
            }
          />
        )}
      </section>

      {isAddOpen && (
        <AddTeamMemberDialog
          onAccessDenied={denyAccess}
          onAdded={(member) => {
            setIsAddOpen(false);
            setNotice(
              `${member.name} was added as ${teamRoleLabels[member.role]}.`
            );
          }}
          onClose={() => setIsAddOpen(false)}
          teamSlug={teamSlug}
          userId={currentUser.id}
        />
      )}
    </section>
  );
}

function TeamMembersError({ error }: { error: unknown }) {
  const { teamSlug } = Route.useParams();
  const isForbidden = error instanceof HttpError && error.status === 403;
  const isNotFound = error instanceof HttpError && error.status === 404;
  return (
    <section className="max-w-2xl">
      <h2 className="text-2xl font-semibold text-primary">
        {isForbidden
          ? 'Not authorized'
          : isNotFound
            ? 'Team not found'
            : 'We could not load team members'}
      </h2>
      <p className="mt-4 text-base-content/70">
        {isForbidden
          ? "You don't have permission to manage members of this team."
          : isNotFound
            ? 'Check the team URL and try again.'
            : 'Refresh the page or try again later.'}
      </p>
      <Link
        className="btn btn-outline mt-6"
        params={{ teamSlug }}
        to="/teams/$teamSlug"
      >
        Back to team overview
      </Link>
    </section>
  );
}
