import { MagnifyingGlassIcon, UserPlusIcon } from '@heroicons/react/24/outline';
import { useForm, useStore } from '@tanstack/react-form';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import type { TeamMember } from './models/TeamMember.ts';
import {
  assignableTeamRoles,
  TeamRole,
  teamRoleLabels,
} from './models/TeamRole.ts';
import { HttpError } from '@/lib/api.ts';
import {
  addTeamMember,
  invalidateTeamMemberQueries,
  teamPeopleQueryOptions,
} from '@/queries/teams.ts';

export function AddTeamMemberDialog({
  onAccessDenied,
  onAdded,
  onClose,
  teamSlug,
  userId,
}: {
  onAccessDenied: () => void;
  onAdded: (member: TeamMember) => void;
  onClose: () => void;
  teamSlug: string;
  userId: string;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [searchText, setSearchText] = useState('');
  const [submittedQuery, setSubmittedQuery] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const peopleQuery = useQuery(
    teamPeopleQueryOptions(teamSlug, userId, submittedQuery)
  );
  const people = submittedQuery ? peopleQuery.data : undefined;
  const findSelectedPerson = (iamId: string | null) =>
    people?.find(
      (person) =>
        person.isActive &&
        person.isActiveInIam !== false &&
        person.role === null &&
        (person.iamId === iamId || (iamId === null && people.length === 1))
    );
  const addMutation = useMutation({
    mutationFn: ({ iamId, role }: { iamId: string; role: TeamRole }) =>
      addTeamMember(teamSlug, iamId, role),
    onError: (error) => {
      if (error instanceof HttpError && error.status === 403) {
        onAccessDenied();
      }
    },
    onSuccess: async (member) => {
      await invalidateTeamMemberQueries(queryClient, teamSlug);
      onAdded(member);
    },
  });
  const form = useForm({
    defaultValues: {
      role: TeamRole.Editor as TeamRole,
      selectedIamId: null as string | null,
    },
    onSubmit: async ({ value }) => {
      const person = findSelectedPerson(value.selectedIamId);
      if (
        !person ||
        peopleQuery.isFetching ||
        peopleQuery.isError ||
        addMutation.isPending
      ) {
        return;
      }
      try {
        await addMutation.mutateAsync({ iamId: person.iamId, role: value.role });
      } catch {
        // Keep the selected person and role available when adding fails.
      }
    },
  });
  const selectedIamId = useStore(
    form.store,
    (state) => state.values.selectedIamId
  );
  const isSubmitting = useStore(form.store, (state) => state.isSubmitting);
  const isPending = isSubmitting || addMutation.isPending;
  const selectedPerson = findSelectedPerson(selectedIamId);

  useEffect(() => {
    const dialog = dialogRef.current;
    const previousFocus = document.activeElement as HTMLElement | null;
    dialog?.showModal();

    return () => {
      dialog?.close();
      if (previousFocus?.isConnected) {
        previousFocus.focus?.();
      }
    };
  }, []);

  useEffect(() => {
    if (
      peopleQuery.error instanceof HttpError &&
      peopleQuery.error.status === 403
    ) {
      onAccessDenied();
    }
  }, [onAccessDenied, peopleQuery.error]);

  function searchPeople(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const query = searchText.trim();
    if (!query || isPending) {
      return;
    }

    form.setFieldValue('selectedIamId', null);
    addMutation.reset();
    if (query === submittedQuery) {
      void peopleQuery.refetch();
    } else {
      setSubmittedQuery(query);
    }
  }

  function searchAgain() {
    setSubmittedQuery(null);
    form.setFieldValue('selectedIamId', null);
    addMutation.reset();
    inputRef.current?.focus();
    inputRef.current?.select();
  }

  const canAdd =
    selectedPerson &&
    !peopleQuery.isFetching &&
    !peopleQuery.isError &&
    !isPending;
  let addError =
    'We could not add this team member. Search again to check their current details, then retry.';
  if (addMutation.error instanceof HttpError) {
    if (addMutation.error.status === 409) {
      addError =
        'This person is already a team member or is no longer eligible. Search again to check their current status.';
    } else if (addMutation.error.status === 400) {
      addError =
        'This person cannot be added. Search again to check their current status.';
    }
  }

  return (
    <dialog
      aria-describedby="add-team-member-description"
      aria-labelledby="add-team-member-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!isPending) {
          onClose();
        }
      }}
      ref={dialogRef}
    >
      <div className="modal-box max-w-2xl">
        <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <UserPlusIcon aria-hidden="true" className="h-6 w-6" />
        </div>
        <h2
          className="text-2xl font-semibold text-primary"
          id="add-team-member-title"
        >
          Add a team member
        </h2>
        <p
          className="mt-2 text-base-content/70"
          id="add-team-member-description"
        >
          Find a person, review their status, and choose their role on this
          team.
        </p>

        <form className="mt-6" onSubmit={searchPeople}>
          <label
            className="mb-2 block text-sm font-semibold"
            htmlFor="team-person-search"
          >
            Email, IAM ID, or Kerberos ID
          </label>
          <div className="flex flex-col gap-3 sm:flex-row">
            <input
              autoComplete="off"
              className="input input-bordered w-full"
              disabled={isPending}
              id="team-person-search"
              maxLength={128}
              onChange={(event) => {
                setSearchText(event.target.value);
                setSubmittedQuery(null);
                form.setFieldValue('selectedIamId', null);
                addMutation.reset();
              }}
              placeholder="Enter a complete email or ID"
              ref={inputRef}
              required
              type="text"
              value={searchText}
            />
            <button
              className="btn btn-primary"
              disabled={
                !searchText.trim() ||
                peopleQuery.isFetching ||
                isPending
              }
              type="submit"
            >
              <MagnifyingGlassIcon aria-hidden="true" className="h-4 w-4" />
              {peopleQuery.isFetching ? 'Searching…' : 'Search'}
            </button>
          </div>
          <p className="mt-2 text-sm text-base-content/60">
            Use the exact email address or ID. Names and partial matches are not
            searched.
          </p>
        </form>

        <div aria-live="polite" className="mt-5">
          {submittedQuery && peopleQuery.isFetching && (
            <p className="text-base-content/70" role="status">
              Searching for a matching person…
            </p>
          )}
          {submittedQuery && peopleQuery.isError && (
            <p className="alert alert-error" role="alert">
              We could not search for that person. Please try again.
            </p>
          )}
          {people &&
            !peopleQuery.isFetching &&
            !peopleQuery.isError &&
            (people.length === 0 ? (
              <p className="rounded-lg bg-base-200 p-4" role="status">
                No people matched that email, IAM ID, or Kerberos ID.
              </p>
            ) : (
              <fieldset>
                <legend className="mb-3 font-semibold">
                  {people.length === 1
                    ? 'Review the matching person'
                    : 'Select a matching person'}
                </legend>
                {people.length === 10 && (
                  <p className="mb-3 text-sm text-base-content/70">
                    Showing the first 10 matches. Search by IAM ID or Kerberos
                    ID to narrow the results.
                  </p>
                )}
                <div className="space-y-3">
                  {people.map((person) => (
                    <label
                      className={`flex gap-3 rounded-xl border p-4 ${selectedPerson?.iamId === person.iamId ? 'border-primary bg-primary/5' : 'border-base-300'}`}
                      key={person.iamId}
                    >
                      <input
                        aria-label={person.name}
                        checked={selectedPerson?.iamId === person.iamId}
                        className="radio radio-primary mt-1 shrink-0"
                        disabled={
                          person.role !== null ||
                          !person.isActive ||
                          person.isActiveInIam === false ||
                          isPending
                        }
                        form="add-team-member-form"
                        name="team-person"
                        onChange={() => {
                          form.setFieldValue('selectedIamId', person.iamId);
                          addMutation.reset();
                        }}
                        type="radio"
                        value={person.iamId}
                      />
                      <div className="min-w-0">
                        <p className="font-semibold">{person.name}</p>
                        <dl className="mt-2 grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
                          <dt className="text-base-content/60">Email</dt>
                          <dd className="break-all">
                            {person.email || 'Not available'}
                          </dd>
                          <dt className="text-base-content/60">IAM ID</dt>
                          <dd>{person.iamId}</dd>
                          <dt className="text-base-content/60">IAM status</dt>
                          <dd>
                            {person.isActiveInIam === null ? (
                              <span className="badge badge-neutral badge-sm">
                                Not checked
                              </span>
                            ) : (
                              <span
                                className={`badge badge-sm ${person.isActiveInIam ? 'badge-success' : 'badge-error'}`}
                              >
                                {person.isActiveInIam ? 'Active' : 'Inactive'}
                              </span>
                            )}
                          </dd>
                          <dt className="text-base-content/60">Kerberos ID</dt>
                          <dd>{person.kerberos || 'Not available'}</dd>
                        </dl>
                        {person.role !== null && (
                          <p className="mt-3 text-sm font-medium">
                            Already a team member ({teamRoleLabels[person.role]}
                            )
                          </p>
                        )}
                        {person.isActiveInIam === false && (
                          <p className="mt-3 text-sm text-error">
                            This person is inactive in IAM and cannot be added.
                          </p>
                        )}
                        {!person.isActive && (
                          <p className="mt-3 text-sm text-error">
                            This user is inactive and cannot be added.
                          </p>
                        )}
                      </div>
                    </label>
                  ))}
                </div>
              </fieldset>
            ))}
        </div>

        <form
          className="mt-6"
          id="add-team-member-form"
          onSubmit={(event) => {
            event.preventDefault();
            event.stopPropagation();
            void form.handleSubmit();
          }}
        >
          <form.Field name="role">
            {(field) => (
              <>
                <label
                  className="mb-2 block text-sm font-semibold"
                  htmlFor="add-team-member-role"
                >
                  Team role
                </label>
                <select
                  className="select select-bordered w-full"
                  disabled={isPending}
                  id="add-team-member-role"
                  onBlur={field.handleBlur}
                  onChange={(event) => {
                    const selectedRole = assignableTeamRoles.find(
                      (value) => value === event.target.value
                    );
                    if (selectedRole) {
                      field.handleChange(selectedRole);
                      addMutation.reset();
                    }
                  }}
                  value={field.state.value}
                >
                  {assignableTeamRoles.map((value) => (
                    <option key={value} value={value}>
                      {teamRoleLabels[value]}
                    </option>
                  ))}
                </select>
                <p className="mt-2 text-sm text-base-content/65">
                  {field.state.value === TeamRole.Admin
                    ? 'Admins can manage this team and its members.'
                    : 'Editors have team access but cannot manage members.'}
                </p>
              </>
            )}
          </form.Field>
        </form>

        {addMutation.isError && (
          <p className="alert alert-error mt-4" role="alert">
            {addError}
          </p>
        )}

        <div className="modal-action flex-wrap">
          <button
            className="btn btn-ghost"
            disabled={isPending}
            onClick={onClose}
            type="button"
          >
            Cancel
          </button>
          {submittedQuery && (
            <button
              className="btn btn-outline"
              disabled={isPending}
              onClick={searchAgain}
              type="button"
            >
              Search again
            </button>
          )}
          <button
            className="btn btn-primary"
            disabled={!canAdd}
            form="add-team-member-form"
            type="submit"
          >
            {isPending ? 'Adding…' : 'Add member'}
          </button>
        </div>
      </div>
    </dialog>
  );
}
