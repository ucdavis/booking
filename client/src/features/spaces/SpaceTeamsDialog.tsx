import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import type { Space } from './models/Space.ts';
import type { SpaceTeamAccess } from './models/SpaceTeamAccess.ts';
import type { TeamSummary } from '@/features/teams/models/TeamSummary.ts';
import { HttpError } from '@/lib/api.ts';
import {
  assignSpaceTeam,
  spaceTeamsQueryOptions,
  unlinkSpaceTeam,
} from '@/queries/admin-spaces.ts';
import { adminTeamsQueryOptions } from '@/queries/admin.ts';
import { resourceOptionsQueryOptions } from '@/queries/resources.ts';
import { FieldWrapper } from '@/shared/forms/fieldWrapper.tsx';
import { useAppForm } from '@/shared/forms/formContext.tsx';

function confirmationResourceCount(error: unknown): number | undefined {
  if (!(error instanceof HttpError) || error.status !== 409) {
    return undefined;
  }
  let details = error.body;
  if (typeof details === 'string') {
    try {
      details = JSON.parse(details);
    } catch {
      return undefined;
    }
  }
  if (
    details !== null &&
    typeof details === 'object' &&
    'code' in details &&
    details.code === 'space_team_resources_confirmation_required' &&
    'resourceCount' in details &&
    typeof details.resourceCount === 'number' &&
    Number.isSafeInteger(details.resourceCount) &&
    details.resourceCount >= 0
  ) {
    return details.resourceCount;
  }
  return undefined;
}

export function SpaceTeamsDialog({
  onClose,
  space,
}: {
  onClose: () => void;
  space: Space;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const cancelUnlinkRef = useRef<HTMLButtonElement>(null);
  const mutationInProgressRef = useRef(false);
  const [confirmation, setConfirmation] = useState<SpaceTeamAccess | null>(
    null
  );
  const [countChanged, setCountChanged] = useState(false);
  const [notice, setNotice] = useState('');
  const [actionError, setActionError] = useState('');
  const [permissionDenied, setPermissionDenied] = useState(false);
  const queryClient = useQueryClient();
  const linkedTeamsQuery = useQuery(spaceTeamsQueryOptions(space.id));
  const teamsQuery = useQuery(adminTeamsQueryOptions());
  const accessDenied =
    permissionDenied ||
    [linkedTeamsQuery.error, teamsQuery.error].some(
      (error) => error instanceof HttpError && error.status === 403
    );
  const availableTeams = (teamsQuery.data ?? []).filter(
    (team) =>
      !linkedTeamsQuery.data?.some((access) => access.team.id === team.id)
  );

  async function refreshTeamAccess(team: TeamSummary) {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: spaceTeamsQueryOptions(space.id).queryKey,
      }),
      queryClient.invalidateQueries({
        queryKey: resourceOptionsQueryOptions(team.slug).queryKey,
      }),
    ]);
  }

  const assignMutation = useMutation({
    mutationFn: (team: TeamSummary) => assignSpaceTeam(space.id, team.id),
    onError: (error) => {
      if (error instanceof HttpError && error.status === 403) {
        setPermissionDenied(true);
      } else {
        setActionError('We could not assign the team. Please try again.');
      }
    },
    onSuccess: async (access) => {
      queryClient.setQueryData<SpaceTeamAccess[]>(
        spaceTeamsQueryOptions(space.id).queryKey,
        (current) => [
          ...(current ?? []).filter((item) => item.team.id !== access.team.id),
          access,
        ]
      );
      await refreshTeamAccess(access.team);
      setNotice(`${access.team.name} assigned to ${space.name}.`);
    },
  });
  const unlinkMutation = useMutation({
    mutationFn: ({
      access,
      confirmedResourceCount,
    }: {
      access: SpaceTeamAccess;
      confirmedResourceCount?: number;
    }) => unlinkSpaceTeam(space.id, access.team.id, confirmedResourceCount),
    onError: async (error, { access }) => {
      if (error instanceof HttpError && error.status === 403) {
        setPermissionDenied(true);
        return;
      }
      const resourceCount = confirmationResourceCount(error);
      if (resourceCount !== undefined) {
        setConfirmation({ ...access, resourceCount });
        setCountChanged(true);
        queryClient.setQueryData<SpaceTeamAccess[]>(
          spaceTeamsQueryOptions(space.id).queryKey,
          (current) =>
            current?.map((item) =>
              item.team.id === access.team.id
                ? { ...item, resourceCount }
                : item
            )
        );
        await refreshTeamAccess(access.team);
      } else {
        setActionError('We could not unlink the team. Please try again.');
      }
    },
    onSuccess: async (_data, { access }) => {
      queryClient.setQueryData<SpaceTeamAccess[]>(
        spaceTeamsQueryOptions(space.id).queryKey,
        (current) => current?.filter((item) => item.team.id !== access.team.id)
      );
      setConfirmation(null);
      setCountChanged(false);
      await refreshTeamAccess(access.team);
      setNotice(`${access.team.name} unlinked from ${space.name}.`);
    },
  });
  const isMutating = assignMutation.isPending || unlinkMutation.isPending;
  const canAssign =
    !accessDenied &&
    linkedTeamsQuery.isSuccess &&
    teamsQuery.isSuccess &&
    !linkedTeamsQuery.isFetching &&
    !teamsQuery.isFetching &&
    !confirmation;
  const form = useAppForm({
    defaultValues: { teamId: '' },
    onSubmit: async ({ formApi, value }) => {
      const team = availableTeams.find(
        (item) => String(item.id) === value.teamId
      );
      if (!canAssign || !team || mutationInProgressRef.current) {
        return;
      }
      mutationInProgressRef.current = true;
      setActionError('');
      setNotice('');
      try {
        await assignMutation.mutateAsync(team);
        formApi.reset();
      } catch {
        // Keep the selected team after a failed assignment.
      } finally {
        mutationInProgressRef.current = false;
      }
    },
  });

  useEffect(() => {
    const dialog = dialogRef.current;
    const previousFocus = document.activeElement as HTMLElement | null;
    dialog?.showModal();
    closeButtonRef.current?.focus();
    return () => {
      dialog?.close();
      if (previousFocus?.isConnected) {
        previousFocus.focus?.();
      }
    };
  }, []);

  useEffect(() => {
    if (isMutating) {
      return;
    }
    if (accessDenied || notice) {
      closeButtonRef.current?.focus();
    } else if (confirmation) {
      cancelUnlinkRef.current?.focus();
    }
  }, [accessDenied, confirmation, isMutating, notice]);

  async function unlink(
    access: SpaceTeamAccess,
    confirmedResourceCount?: number
  ) {
    if (accessDenied || mutationInProgressRef.current) {
      return;
    }
    mutationInProgressRef.current = true;
    setActionError('');
    setNotice('');
    try {
      await unlinkMutation.mutateAsync({ access, confirmedResourceCount });
    } catch {
      // Errors and fresh confirmation requirements are displayed in the dialog.
    } finally {
      mutationInProgressRef.current = false;
    }
  }

  return (
    <dialog
      aria-describedby="space-teams-description"
      aria-labelledby="space-teams-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!mutationInProgressRef.current) {
          onClose();
        }
      }}
      ref={dialogRef}
    >
      <div className="modal-box max-w-3xl">
        <h2
          className="text-2xl font-semibold text-primary"
          id="space-teams-title"
        >
          Manage teams for {space.name}
        </h2>
        <p className="mt-2 text-base-content/70" id="space-teams-description">
          Assigned teams can create resources in this space while it is active.
        </p>
        {accessDenied ? (
          <p className="alert alert-error mt-6" role="alert">
            You no longer have permission to manage teams for this space.
          </p>
        ) : (
          <>
            {!space.isActive && (
              <p className="alert alert-warning mt-5" role="status">
                This space is inactive. Teams cannot create new resources here
                until it is active.
              </p>
            )}
            {notice && (
              <p className="alert alert-success mt-5" role="status">
                {notice}
              </p>
            )}
            <section
              aria-labelledby="space-assigned-teams-title"
              className="mt-6"
            >
              <h3
                className="text-lg font-semibold"
                id="space-assigned-teams-title"
              >
                Assigned teams
              </h3>
              {linkedTeamsQuery.isPending ? (
                <p className="mt-3" role="status">
                  Loading assigned teams…
                </p>
              ) : linkedTeamsQuery.isError ? (
                <div className="mt-3">
                  <p role="alert">
                    We could not load the assigned teams. Please try again.
                  </p>
                  <button
                    className="btn btn-outline btn-sm mt-3"
                    disabled={isMutating || linkedTeamsQuery.isFetching}
                    onClick={() => void linkedTeamsQuery.refetch()}
                    type="button"
                  >
                    Retry assigned teams
                  </button>
                </div>
              ) : linkedTeamsQuery.data.length === 0 ? (
                <p className="mt-3 text-base-content/70">
                  No teams are assigned to this space.
                </p>
              ) : (
                <div className="mt-3 overflow-x-auto">
                  <table aria-label="Assigned teams" className="table w-full">
                    <thead>
                      <tr>
                        <th scope="col">Team</th>
                        <th scope="col">Resources in this space</th>
                        <th scope="col">Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {linkedTeamsQuery.data.map((access) => (
                        <tr key={access.team.id}>
                          <th scope="row">{access.team.name}</th>
                          <td>{access.resourceCount}</td>
                          <td>
                            <button
                              aria-label={`Unlink ${access.team.name}`}
                              className="btn btn-ghost btn-sm text-error"
                              disabled={
                                isMutating ||
                                !!confirmation ||
                                linkedTeamsQuery.isFetching
                              }
                              onClick={() => {
                                if (mutationInProgressRef.current) {
                                  return;
                                }
                                setActionError('');
                                setNotice('');
                                setCountChanged(false);
                                if (access.resourceCount > 0) {
                                  setConfirmation(access);
                                } else {
                                  void unlink(access);
                                }
                              }}
                              type="button"
                            >
                              Unlink
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            {confirmation && (
              <section
                className="mt-5 rounded-sm border border-warning bg-warning/10 p-4"
                role="alert"
              >
                <h3 className="font-semibold">
                  Unlink {confirmation.team.name}?
                </h3>
                {countChanged && (
                  <p className="mt-2">
                    The resource count changed. Review the current count and
                    confirm again.
                  </p>
                )}
                <p className="mt-2">
                  {confirmation.team.name} has {confirmation.resourceCount}{' '}
                  {confirmation.resourceCount === 1 ? 'resource' : 'resources'}{' '}
                  in {space.name}.
                </p>
                <p className="mt-2">
                  Unlinking blocks this team from creating new resources here.
                  Existing resources and reservation records remain, and the
                  team can still manage its existing resources.
                </p>
                <div className="mt-4 flex flex-wrap gap-3">
                  <button
                    className="btn btn-ghost btn-sm"
                    disabled={isMutating}
                    onClick={() => {
                      setConfirmation(null);
                      setCountChanged(false);
                      setActionError('');
                      closeButtonRef.current?.focus();
                    }}
                    ref={cancelUnlinkRef}
                    type="button"
                  >
                    Cancel unlink
                  </button>
                  <button
                    className="btn btn-error btn-sm"
                    disabled={isMutating}
                    onClick={() =>
                      void unlink(confirmation, confirmation.resourceCount)
                    }
                    type="button"
                  >
                    Confirm unlink
                  </button>
                </div>
              </section>
            )}

            <section aria-labelledby="space-assign-team-title" className="mt-6">
              <h3
                className="text-lg font-semibold"
                id="space-assign-team-title"
              >
                Assign a team
              </h3>
              {teamsQuery.isPending ? (
                <p className="mt-3" role="status">
                  Loading teams…
                </p>
              ) : teamsQuery.isError ? (
                <div className="mt-3">
                  <p role="alert">
                    We could not load the teams. Please try again.
                  </p>
                  <button
                    className="btn btn-outline btn-sm mt-3"
                    disabled={isMutating || teamsQuery.isFetching}
                    onClick={() => void teamsQuery.refetch()}
                    type="button"
                  >
                    Retry teams
                  </button>
                </div>
              ) : linkedTeamsQuery.isSuccess && availableTeams.length === 0 ? (
                <p className="mt-3 text-base-content/70">
                  No unassigned teams are available.
                </p>
              ) : (
                <form
                  className="mt-3"
                  noValidate
                  onSubmit={(event) => {
                    event.preventDefault();
                    if (!mutationInProgressRef.current && canAssign) {
                      void form.handleSubmit();
                    }
                  }}
                >
                  <fieldset
                    className="flex flex-col gap-3 sm:flex-row sm:items-end"
                    disabled={isMutating || !canAssign}
                  >
                    <div className="grow">
                      <form.AppField
                        name="teamId"
                        validators={{
                          onChange: ({ value }) =>
                            availableTeams.some(
                              (team) => String(team.id) === value
                            )
                              ? undefined
                              : 'Choose a team to assign.',
                        }}
                      >
                        {(field) => {
                          const hasError =
                            field.state.meta.isTouched &&
                            !field.state.meta.isValid;
                          return (
                            <FieldWrapper
                              controlId="space-assignment-team"
                              label="Team"
                            >
                              <select
                                aria-describedby={
                                  hasError
                                    ? 'space-assignment-team-error'
                                    : undefined
                                }
                                aria-invalid={hasError || undefined}
                                className={`select select-bordered w-full ${hasError ? 'select-error' : ''}`}
                                id="space-assignment-team"
                                name={field.name}
                                onBlur={field.handleBlur}
                                onChange={(event) => {
                                  field.handleChange(event.target.value);
                                  setActionError('');
                                  setNotice('');
                                }}
                                required
                                value={
                                  availableTeams.some(
                                    (team) =>
                                      String(team.id) === field.state.value
                                  )
                                    ? field.state.value
                                    : ''
                                }
                              >
                                <option disabled value="">
                                  Pick a team
                                </option>
                                {availableTeams.map((team) => (
                                  <option key={team.id} value={team.id}>
                                    {team.name}
                                  </option>
                                ))}
                              </select>
                            </FieldWrapper>
                          );
                        }}
                      </form.AppField>
                    </div>
                    <form.Subscribe
                      selector={(state) =>
                        [
                          state.canSubmit,
                          state.isSubmitting,
                          state.values.teamId,
                        ] as const
                      }
                    >
                      {([canSubmit, isSubmitting, teamId]) => (
                        <button
                          className="btn btn-primary"
                          disabled={
                            !canSubmit ||
                            isSubmitting ||
                            !availableTeams.some(
                              (team) => String(team.id) === teamId
                            )
                          }
                          type="submit"
                        >
                          {assignMutation.isPending
                            ? 'Assigning…'
                            : 'Assign team'}
                        </button>
                      )}
                    </form.Subscribe>
                  </fieldset>
                </form>
              )}
            </section>
            {actionError && (
              <p className="alert alert-error mt-5" role="alert">
                {actionError}
              </p>
            )}
          </>
        )}
        <div className="modal-action">
          <button
            className="btn btn-ghost"
            disabled={isMutating}
            onClick={() => {
              if (!mutationInProgressRef.current) {
                onClose();
              }
            }}
            ref={closeButtonRef}
            type="button"
          >
            Close
          </button>
        </div>
      </div>
    </dialog>
  );
}
