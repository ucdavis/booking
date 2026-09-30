import { MagnifyingGlassIcon, UserPlusIcon } from '@heroicons/react/24/outline';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import type { AdminUser } from './models/AdminUser.ts';
import { HttpError } from '@/lib/api.ts';
import {
  addAdminUser,
  adminPeopleQueryOptions,
  adminUsersQueryKey,
} from '@/queries/admin.ts';

export function AddAdminUserDialog({
  onAdded,
  onClose,
}: {
  onAdded: (user: AdminUser) => void;
  onClose: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [searchText, setSearchText] = useState('');
  const [submittedQuery, setSubmittedQuery] = useState<string | null>(null);
  const [selectedIamId, setSelectedIamId] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const peopleQuery = useQuery(adminPeopleQueryOptions(submittedQuery));
  const people = submittedQuery ? peopleQuery.data : undefined;
  const selectedPerson = people?.find(
    (person) =>
      person.isActive &&
      person.isActiveInIam &&
      !person.isAdmin &&
      (person.iamId === selectedIamId ||
        (selectedIamId === null && people.length === 1))
  );
  const addMutation = useMutation({
    mutationFn: addAdminUser,
    onSuccess: async (user) => {
      await queryClient.invalidateQueries({ queryKey: adminUsersQueryKey });
      onAdded(user);
    },
  });

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

  function searchPeople(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const query = searchText.trim();
    if (!query || addMutation.isPending) {
      return;
    }

    setSelectedIamId(null);
    addMutation.reset();
    if (query === submittedQuery) {
      void peopleQuery.refetch();
    } else {
      setSubmittedQuery(query);
    }
  }

  function searchAgain() {
    setSubmittedQuery(null);
    setSelectedIamId(null);
    addMutation.reset();
    inputRef.current?.focus();
    inputRef.current?.select();
  }

  const canAdd =
    selectedPerson?.isActive &&
    selectedPerson.isActiveInIam &&
    !selectedPerson.isAdmin &&
    !peopleQuery.isFetching &&
    !peopleQuery.isError &&
    !addMutation.isPending;

  return (
    <dialog
      aria-describedby="add-admin-description"
      aria-labelledby="add-admin-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!addMutation.isPending) {
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
          id="add-admin-title"
        >
          Add a site admin user
        </h2>
        <p className="mt-2 text-base-content/70" id="add-admin-description">
          Find a person, review their details, and grant access to site
          administration.
        </p>

        <form className="mt-6" onSubmit={searchPeople}>
          <label
            className="mb-2 block text-sm font-semibold"
            htmlFor="admin-person-search"
          >
            Email, IAM ID, or Kerberos ID
          </label>
          <div className="flex flex-col gap-3 sm:flex-row">
            <input
              autoComplete="off"
              className="input input-bordered w-full"
              disabled={addMutation.isPending}
              id="admin-person-search"
              maxLength={128}
              onChange={(event) => {
                setSearchText(event.target.value);
                setSubmittedQuery(null);
                setSelectedIamId(null);
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
                addMutation.isPending
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
              {peopleQuery.error instanceof HttpError &&
              peopleQuery.error.status === 403
                ? 'You no longer have permission to manage site admin users.'
                : 'We could not search for that person. Please try again.'}
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
                          person.isAdmin ||
                          !person.isActive ||
                          !person.isActiveInIam ||
                          addMutation.isPending
                        }
                        name="admin-person"
                        onChange={() => {
                          setSelectedIamId(person.iamId);
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
                            <span
                              className={`badge badge-sm ${person.isActiveInIam ? 'badge-success' : 'badge-error'}`}
                            >
                              {person.isActiveInIam ? 'Active' : 'Inactive'}
                            </span>
                          </dd>
                          <dt className="text-base-content/60">Kerberos ID</dt>
                          <dd>{person.kerberos || 'Not available'}</dd>
                        </dl>
                        {person.isAdmin && (
                          <p className="mt-3 text-sm font-medium">
                            Already a site admin user
                          </p>
                        )}
                        {!person.isActiveInIam && (
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
                {selectedPerson &&
                  !selectedPerson.isAdmin &&
                  selectedPerson.isActive &&
                  selectedPerson.isActiveInIam && (
                    <p className="mt-4 text-sm text-base-content/70">
                      Adding {selectedPerson.name} grants access to site
                      administration, including managing other site admin users.
                    </p>
                  )}
              </fieldset>
            ))}
        </div>

        {addMutation.isError && (
          <p className="alert alert-error mt-4" role="alert">
            {addMutation.error instanceof HttpError &&
            addMutation.error.status === 403
              ? 'You no longer have permission to manage site admin users.'
              : 'We could not add this admin user. Search again to check their current details, then retry.'}
          </p>
        )}

        <div className="modal-action flex-wrap">
          <button
            className="btn btn-ghost"
            disabled={addMutation.isPending}
            onClick={onClose}
            type="button"
          >
            Cancel
          </button>
          {submittedQuery && (
            <button
              className="btn btn-outline"
              disabled={addMutation.isPending}
              onClick={searchAgain}
              type="button"
            >
              Search again
            </button>
          )}
          <button
            className="btn btn-primary"
            disabled={!canAdd}
            onClick={() => {
              if (canAdd && selectedPerson) {
                addMutation.mutate(selectedPerson.iamId);
              }
            }}
            type="button"
          >
            {addMutation.isPending ? 'Adding…' : 'Add admin user'}
          </button>
        </div>
      </div>
    </dialog>
  );
}
