import { UserPlusIcon } from '@heroicons/react/24/outline';
import {
  useIsMutating,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import {
  createFileRoute,
  Link,
  redirect,
  type ErrorComponentProps,
} from '@tanstack/react-router';
import type { ColumnDef } from '@tanstack/react-table';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { AddTeamMemberDialog } from '@/features/teams/AddTeamMemberDialog.tsx';
import { EditTeamMemberRolesDialog } from '@/features/teams/EditTeamMemberRolesDialog.tsx';
import { RemoveTeamMemberDialog } from '@/features/teams/RemoveTeamMemberDialog.tsx';
import type { TeamMember } from '@/features/teams/models/TeamMember.ts';
import { TeamRole, teamRoleLabels } from '@/features/teams/models/TeamRole.ts';
import { HttpError } from '@/lib/api.ts';
import {
  changeTeamMemberRoles,
  invalidateTeamMemberQueries,
  removeTeamMember,
  teamAccessQueryOptions,
  teamMemberMutationKey,
  teamMembersQueryKey,
  teamMembersQueryOptions,
  teamSelfAccessMutationFilter,
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
  const navigate = Route.useNavigate();
  const currentUser = useUser();
  const queryClient = useQueryClient();
  const isChangingOwnAccess =
    useIsMutating(teamSelfAccessMutationFilter(teamSlug)) > 0;
  const accessQuery = useQuery({
    ...teamAccessQueryOptions(teamSlug),
    enabled: !isChangingOwnAccess,
    refetchOnMount: false,
  });
  const membersQuery = useQuery({
    ...teamMembersQueryOptions(teamSlug, currentUser.id),
    enabled: !isChangingOwnAccess,
    refetchOnMount: false,
  });
  const [isAddOpen, setIsAddOpen] = useState(false);
  const [editingMember, setEditingMember] = useState<TeamMember | null>(null);
  const [removingMember, setRemovingMember] = useState<TeamMember | null>(null);
  const [notice, setNotice] = useState('');
  const [accessDenied, setAccessDenied] = useState(false);
  const denyAccess = useCallback(() => {
    setAccessDenied(true);
    void queryClient.invalidateQueries({
      queryKey: ['teams', 'access', teamSlug],
    });
  }, [queryClient, teamSlug]);
  const isCurrentUser = useCallback(
    (member: TeamMember) =>
      member.iamId.trim().toLowerCase() ===
      currentUser.iamId?.trim().toLowerCase(),
    [currentUser.iamId]
  );
  const isSiteAdmin = accessQuery.data?.isSiteAdmin ?? currentUser.isSiteAdmin;
  const pauseOwnAccessReads = async ({
    changesOwnAccess,
  }: {
    changesOwnAccess: boolean;
  }) => {
    if (changesOwnAccess) {
      await Promise.all([
        queryClient.cancelQueries({ queryKey: teamMembersQueryKey(teamSlug) }),
        queryClient.cancelQueries({ queryKey: ['teams', 'access', teamSlug] }),
        queryClient.cancelQueries({ queryKey: ['teams', 'people', teamSlug] }),
      ]);
    }
  };
  const leaveMemberManagement = async (removed: boolean) => {
    await invalidateTeamMemberQueries(queryClient, teamSlug, 'none');
    await (removed
      ? navigate({ replace: true, to: '/me' })
      : navigate({
          params: { teamSlug },
          replace: true,
          to: '/teams/$teamSlug',
        }));
    queryClient.removeQueries({ queryKey: teamMembersQueryKey(teamSlug) });
    queryClient.removeQueries({ queryKey: ['teams', 'people', teamSlug] });
    if (removed) {
      queryClient.removeQueries({ queryKey: ['teams', 'access', teamSlug] });
    }
    await queryClient.invalidateQueries({ queryKey: ['teams', 'memberships'] });
  };
  const roleMutation = useMutation({
    mutationFn: async ({
      changesOwnAccess,
      confirmSelfAccessChange,
      member,
      roles,
    }: {
      changesOwnAccess: boolean;
      confirmSelfAccessChange: boolean;
      member: TeamMember;
      roles: TeamRole[];
    }) => {
      await pauseOwnAccessReads({ changesOwnAccess });
      return changeTeamMemberRoles(
        teamSlug,
        member.id,
        roles,
        confirmSelfAccessChange
      );
    },
    mutationKey: teamMemberMutationKey(teamSlug),
    onError: (error) => {
      if (error instanceof HttpError && error.status === 403) {
        denyAccess();
      }
    },
    onSuccess: async (member, { changesOwnAccess }) => {
      if (changesOwnAccess) {
        await leaveMemberManagement(false);
        return;
      }
      await invalidateTeamMemberQueries(queryClient, teamSlug);
      setEditingMember(null);
      setNotice(
        `${member.name}'s roles are now ${member.roles.map((role) => teamRoleLabels[role]).join(', ')}.`
      );
    },
  });
  const removeMutation = useMutation({
    mutationFn: async ({
      changesOwnAccess,
      confirmSelfAccessChange,
      member,
    }: {
      changesOwnAccess: boolean;
      confirmSelfAccessChange: boolean;
      member: TeamMember;
    }) => {
      await pauseOwnAccessReads({ changesOwnAccess });
      return removeTeamMember(teamSlug, member.id, confirmSelfAccessChange);
    },
    mutationKey: teamMemberMutationKey(teamSlug),
    onError: (error) => {
      if (error instanceof HttpError && error.status === 403) {
        denyAccess();
      }
    },
    onSuccess: async (_data, { changesOwnAccess, member }) => {
      if (changesOwnAccess) {
        await leaveMemberManagement(true);
        return;
      }
      await invalidateTeamMemberQueries(queryClient, teamSlug);
      setRemovingMember(null);
      setNotice(`${member.name} was removed from this team.`);
    },
  });

  useEffect(() => {
    if (isChangingOwnAccess || !(membersQuery.error instanceof HttpError)) {
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
  }, [isChangingOwnAccess, membersQuery.error, queryClient, teamSlug]);

  const isBusy = roleMutation.isPending || removeMutation.isPending;
  const { reset: resetRoleMutation } = roleMutation;
  const { reset: resetRemoveMutation } = removeMutation;
  const openRoleEditor = useCallback(
    (member: TeamMember) => {
      setEditingMember(member);
      setRemovingMember(null);
      setNotice('');
      resetRoleMutation();
    },
    [resetRoleMutation]
  );
  const openMemberRemoval = useCallback(
    (member: TeamMember) => {
      setRemovingMember(member);
      setEditingMember(null);
      setNotice('');
      resetRemoveMutation();
    },
    [resetRemoveMutation]
  );
  const columns = useMemo<ColumnDef<TeamMember>[]>(
    () => [
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
        accessorFn: (member) =>
          member.roles.map((role) => teamRoleLabels[role]).join(', '),
        cell: ({ row }) => (
          <div className="flex flex-wrap gap-1">
            {row.original.roles.map((role) => (
              <span className="badge badge-outline badge-sm" key={role}>
                {teamRoleLabels[role]}
              </span>
            ))}
          </div>
        ),
        header: 'Roles',
        id: 'roles',
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
        cell: ({ row }) => (
          <div className="flex flex-wrap gap-1">
            {isCurrentUser(row.original) && (
              <span className="badge badge-outline self-center">You</span>
            )}
            <button
              aria-label={`Manage roles for ${row.original.name}`}
              className="btn btn-ghost btn-sm"
              onClick={() => openRoleEditor(row.original)}
              type="button"
            >
              Manage roles
            </button>
            <button
              aria-label={`Remove ${row.original.name} from team`}
              className="btn btn-ghost btn-sm text-error"
              onClick={() => openMemberRemoval(row.original)}
              type="button"
            >
              Remove member
            </button>
          </div>
        ),
        enableSorting: false,
        header: 'Actions',
        id: 'actions',
      },
    ],
    [isCurrentUser, openMemberRemoval, openRoleEditor]
  );

  if (
    membersQuery.error instanceof HttpError &&
    membersQuery.error.status === 401
  ) {
    return <p role="status">Redirecting to sign in…</p>;
  }

  const canManage =
    accessQuery.isSuccess &&
    (accessQuery.data.isSiteAdmin ||
      accessQuery.data.roles.includes(TeamRole.Admin));
  if (!isChangingOwnAccess && (accessDenied || !canManage)) {
    return <TeamMembersError error={new HttpError(403, '/api/teams')} />;
  }
  if (membersQuery.isError && !isChangingOwnAccess) {
    return <TeamMembersError error={membersQuery.error} />;
  }
  if (membersQuery.isPending || !membersQuery.data) {
    return <p role="status">Loading team members…</p>;
  }

  return (
    <section>
      <div className="flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h2 className="text-2xl font-semibold text-primary">Team members</h2>
          <p className="mt-3 max-w-2xl text-base-content/70">
            Add people and manage their roles on this team. Changes that remove
            your own administrator access require confirmation.
          </p>
        </div>
        <button
          className="btn btn-primary shrink-0 self-start"
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

      {editingMember && (
        <EditTeamMemberRolesDialog
          hasError={roleMutation.isError}
          isCurrentUser={isCurrentUser(editingMember)}
          isPending={roleMutation.isPending}
          isSiteAdmin={isSiteAdmin}
          key={editingMember.id}
          member={editingMember}
          onChange={() => roleMutation.reset()}
          onClose={() => setEditingMember(null)}
          onSave={async (roles, confirmSelfAccessChange) => {
            await roleMutation.mutateAsync({
              changesOwnAccess:
                isCurrentUser(editingMember) &&
                !isSiteAdmin &&
                !roles.includes(TeamRole.Admin),
              confirmSelfAccessChange,
              member: editingMember,
              roles,
            });
          }}
        />
      )}

      {removingMember && (
        <RemoveTeamMemberDialog
          hasError={removeMutation.isError}
          isCurrentUser={isCurrentUser(removingMember)}
          isPending={removeMutation.isPending}
          isSiteAdmin={isSiteAdmin}
          key={removingMember.id}
          member={removingMember}
          onClose={() => setRemovingMember(null)}
          onConfirm={() =>
            removeMutation.mutate({
              changesOwnAccess: isCurrentUser(removingMember) && !isSiteAdmin,
              confirmSelfAccessChange: isCurrentUser(removingMember),
              member: removingMember,
            })
          }
        />
      )}

      <section
        aria-label="Team members"
        className="mt-8 rounded-sm border border-base-300 bg-base-200 p-4 shadow-sm sm:p-6"
      >
        {membersQuery.data.length === 0 ? (
          <p className="py-8 text-center text-base-content/70">
            This team does not have any members yet.
          </p>
        ) : (
          <fieldset className="min-w-0" disabled={isBusy}>
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
          </fieldset>
        )}
      </section>

      {isAddOpen && (
        <AddTeamMemberDialog
          onAccessDenied={denyAccess}
          onAdded={(member) => {
            setIsAddOpen(false);
            setNotice(
              `${member.name} was added with roles: ${member.roles.map((role) => teamRoleLabels[role]).join(', ')}.`
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
