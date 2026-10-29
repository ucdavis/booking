import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import type { Space } from '@/features/spaces/models/Space.ts';
import type { TeamSummary } from './models/TeamSummary.ts';
import { HttpError } from '@/lib/api.ts';
import {
  adminSpacesQueryOptions,
  assignSpaceTeam,
  confirmationResourceCount,
  unlinkSpaceTeam,
  spaceTeamsQueryOptions,
} from '@/queries/admin-spaces.ts';
import { resourceOptionsQueryOptions } from '@/queries/resources.ts';
import { teamSpacesQueryOptions } from '@/queries/teams.ts';
import { FieldWrapper } from '@/shared/forms/fieldWrapper.tsx';
import { useAppForm } from '@/shared/forms/formContext.tsx';

export function TeamSpaces({
  canAttach,
  team,
}: {
  canAttach: boolean;
  team: TeamSummary;
}) {
  const queryClient = useQueryClient();
  const [isAttaching, setIsAttaching] = useState(false);
  const [notice, setNotice] = useState('');
  const [confirmation, setConfirmation] = useState<{
    resourceCount: number;
    space: Space;
  } | null>(null);
  const [countChanged, setCountChanged] = useState(false);
  const mutationInProgressRef = useRef(false);
  const cancelUnlinkRef = useRef<HTMLButtonElement>(null);
  const attachButtonRef = useRef<HTMLButtonElement>(null);
  const wasConfirmingRef = useRef(false);
  useEffect(() => {
    if (confirmation) {
      cancelUnlinkRef.current?.focus();
    } else if (wasConfirmingRef.current) {
      attachButtonRef.current?.focus();
    }
    wasConfirmingRef.current = !!confirmation;
  }, [confirmation]);
  const spacesQuery = useQuery(teamSpacesQueryOptions(team.slug));
  const availableQuery = useQuery({
    ...adminSpacesQueryOptions(),
    enabled: canAttach && isAttaching,
  });
  const availableSpaces = (availableQuery.data ?? []).filter(
    (space) => !spacesQuery.data?.some((attached) => attached.id === space.id)
  );
  async function refreshSpaces(spaceId: number) {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: teamSpacesQueryOptions(team.slug).queryKey,
      }),
      queryClient.invalidateQueries({
        queryKey: spaceTeamsQueryOptions(spaceId).queryKey,
      }),
      queryClient.invalidateQueries({
        queryKey: resourceOptionsQueryOptions(team.slug).queryKey,
      }),
    ]);
  }
  const unlinkMutation = useMutation({
    mutationFn: ({
      resourceCount,
      space,
    }: {
      resourceCount?: number;
      space: Space;
    }) => unlinkSpaceTeam(space.id, team.id, resourceCount),
    onError: (error, { resourceCount, space }) => {
      const count = confirmationResourceCount(error);
      if (count !== undefined) {
        setConfirmation({ resourceCount: count, space });
        setCountChanged(resourceCount !== undefined);
      }
    },
    onSuccess: async (_result, { space }) => {
      setConfirmation(null);
      setCountChanged(false);
      await refreshSpaces(space.id);
      setNotice(`${space.name} unlinked.`);
      attachButtonRef.current?.focus();
    },
  });
  async function unlink(space: Space, resourceCount?: number) {
    if (!canAttach || mutationInProgressRef.current) {
      return;
    }
    mutationInProgressRef.current = true;
    setNotice('');
    try {
      await unlinkMutation.mutateAsync({ resourceCount, space });
    } catch {
      // Confirmation requirements and errors are shown beside the list.
    } finally {
      mutationInProgressRef.current = false;
    }
  }
  const attachMutation = useMutation({
    mutationFn: (spaceId: number) => assignSpaceTeam(spaceId, team.id),
    onSuccess: async (_access, spaceId) => {
      await refreshSpaces(spaceId);
      setNotice('Space attached.');
      setIsAttaching(false);
    },
  });
  const isMutating = attachMutation.isPending || unlinkMutation.isPending;
  const canSubmit =
    canAttach &&
    spacesQuery.isSuccess &&
    availableQuery.isSuccess &&
    !confirmation;
  const form = useAppForm({
    defaultValues: { spaceId: '' },
    onSubmit: async ({ formApi, value }) => {
      const space = availableSpaces.find(
        (item) => String(item.id) === value.spaceId
      );
      if (!canSubmit || !space || mutationInProgressRef.current) {
        return;
      }
      mutationInProgressRef.current = true;
      setNotice('');
      try {
        await attachMutation.mutateAsync(space.id);
        formApi.reset();
      } catch {
        // Preserve the selection so the user can retry.
      } finally {
        mutationInProgressRef.current = false;
      }
    },
  });

  return (
    <section
      aria-labelledby="team-spaces-title"
      className="min-w-0 rounded-sm border border-base-300 bg-base-200 p-6 shadow-sm sm:p-8"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2
          className="text-xl font-semibold text-primary"
          id="team-spaces-title"
        >
          Attached spaces
        </h2>
        {canAttach && !isAttaching && (
          <button
            className="btn btn-outline btn-sm"
            disabled={isMutating || !!confirmation}
            onClick={() => {
              setNotice('');
              attachMutation.reset();
              setIsAttaching(true);
            }}
            ref={attachButtonRef}
            type="button"
          >
            Attach space
          </button>
        )}
      </div>
      {spacesQuery.isPending ? (
        <p className="mt-5 text-base-content/70" role="status">
          Loading spaces…
        </p>
      ) : spacesQuery.isError ? (
        <div className="mt-5">
          <p role="alert">We could not load this team&apos;s spaces.</p>
          <button
            className="btn btn-outline btn-sm mt-3"
            onClick={() => void spacesQuery.refetch()}
            type="button"
          >
            Retry spaces
          </button>
        </div>
      ) : spacesQuery.data.length === 0 ? (
        <p className="mt-5 text-base-content/70">
          No spaces are attached to this team.
        </p>
      ) : (
        <ul className="mt-5 divide-y divide-base-300">
          {spacesQuery.data.map((space) => (
            <li className="py-3 first:pt-0 last:pb-0" key={space.id}>
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-semibold">{space.name}</span>
                {!space.isActive && (
                  <span className="badge badge-soft">Inactive</span>
                )}
                {canAttach && (
                  <button
                    aria-label={`Unlink ${space.name}`}
                    className="btn btn-ghost btn-sm ml-auto text-error"
                    disabled={
                      isMutating || !!confirmation || spacesQuery.isFetching
                    }
                    onClick={() => {
                      setIsAttaching(false);
                      setCountChanged(false);
                      void unlink(space);
                    }}
                    type="button"
                  >
                    Unlink
                  </button>
                )}
              </div>
              {space.address && (
                <p className="mt-1 text-sm text-base-content/70">
                  {space.address}
                </p>
              )}
            </li>
          ))}
        </ul>
      )}
      {confirmation && (
        <section
          className="mt-5 rounded-sm border border-warning bg-warning/10 p-4"
          role="alert"
        >
          <h3 className="font-semibold">Unlink {confirmation.space.name}?</h3>
          {countChanged && (
            <p className="mt-2">
              The resource count changed. Review the current count and confirm
              again.
            </p>
          )}
          <p className="mt-2">
            This team has {confirmation.resourceCount}{' '}
            {confirmation.resourceCount === 1 ? 'resource' : 'resources'} in
            this space.
          </p>
          <p className="mt-2">
            Unlinking blocks this team from creating new resources here.
            Existing resources and reservation records remain, and the team can
            still manage its existing resources.
          </p>
          <div className="mt-4 flex flex-wrap gap-3">
            <button
              className="btn btn-ghost btn-sm"
              disabled={isMutating}
              onClick={() => {
                setConfirmation(null);
                setCountChanged(false);
                unlinkMutation.reset();
                attachButtonRef.current?.focus();
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
                void unlink(confirmation.space, confirmation.resourceCount)
              }
              type="button"
            >
              Confirm unlink
            </button>
          </div>
        </section>
      )}
      {unlinkMutation.isError &&
        confirmationResourceCount(unlinkMutation.error) === undefined && (
          <p className="alert alert-error mt-5" role="alert">
            {unlinkMutation.error instanceof HttpError &&
            unlinkMutation.error.status === 403
              ? 'Only site administrators can unlink spaces. Your permissions may have changed.'
              : 'We could not unlink this space. Please try again.'}
          </p>
        )}
      {notice && (
        <p className="mt-5 text-success" role="status">
          {notice}
        </p>
      )}
      {canAttach && isAttaching && (
        <div className="mt-6 border-t border-base-300 pt-5">
          {availableQuery.isPending ? (
            <p role="status">Loading available spaces…</p>
          ) : availableQuery.isError ? (
            <div>
              <p role="alert">
                We could not load available spaces. Check your permissions or
                try again.
              </p>
              <button
                className="btn btn-outline btn-sm mt-3"
                onClick={() => void availableQuery.refetch()}
                type="button"
              >
                Retry available spaces
              </button>
            </div>
          ) : spacesQuery.isSuccess && availableSpaces.length === 0 ? (
            <p className="text-base-content/70">
              All available spaces are already attached.
            </p>
          ) : (
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void form.handleSubmit();
              }}
            >
              <fieldset
                className="flex flex-col gap-3 sm:flex-row sm:items-end"
                disabled={!canSubmit || isMutating}
              >
                <div className="grow">
                  <form.AppField
                    name="spaceId"
                    validators={{
                      onChange: ({ value }) =>
                        availableSpaces.some(
                          (space) => String(space.id) === value
                        )
                          ? undefined
                          : 'Choose a space to attach.',
                    }}
                  >
                    {(field) => (
                      <FieldWrapper controlId="team-attach-space" label="Space">
                        <select
                          aria-describedby={
                            field.state.meta.isTouched &&
                            !field.state.meta.isValid
                              ? 'team-attach-space-error'
                              : undefined
                          }
                          aria-invalid={
                            (field.state.meta.isTouched &&
                              !field.state.meta.isValid) ||
                            undefined
                          }
                          className="select w-full"
                          id="team-attach-space"
                          name={field.name}
                          onBlur={field.handleBlur}
                          onChange={(event) => {
                            field.handleChange(event.target.value);
                            attachMutation.reset();
                          }}
                          value={field.state.value}
                        >
                          <option value="">Choose a space</option>
                          {availableSpaces.map((space) => (
                            <option key={space.id} value={space.id}>
                              {space.name}
                              {space.isActive ? '' : ' (inactive)'}
                            </option>
                          ))}
                        </select>
                      </FieldWrapper>
                    )}
                  </form.AppField>
                </div>
                <form.Subscribe
                  selector={(state) =>
                    [state.values.spaceId, state.isSubmitting] as const
                  }
                >
                  {([spaceId, isSubmitting]) => (
                    <button
                      className="btn btn-primary"
                      disabled={
                        isSubmitting ||
                        !availableSpaces.some(
                          (space) => String(space.id) === spaceId
                        )
                      }
                      type="submit"
                    >
                      {attachMutation.isPending ? 'Attaching…' : 'Attach space'}
                    </button>
                  )}
                </form.Subscribe>
              </fieldset>
            </form>
          )}
          {attachMutation.isError && (
            <p className="alert alert-error mt-4" role="alert">
              {attachMutation.error instanceof HttpError &&
              attachMutation.error.status === 403
                ? 'Only site administrators can attach spaces. Your permissions may have changed.'
                : 'We could not attach this space. Please try again.'}
            </p>
          )}
          <button
            className="btn btn-ghost btn-sm mt-3"
            disabled={isMutating}
            onClick={() => {
              setIsAttaching(false);
              form.reset();
              attachMutation.reset();
            }}
            type="button"
          >
            Cancel
          </button>
        </div>
      )}
    </section>
  );
}
