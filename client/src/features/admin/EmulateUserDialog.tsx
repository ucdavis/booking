import { MagnifyingGlassIcon } from '@heroicons/react/24/outline';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import {
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type RefObject,
} from 'react';
import {
  emulationCandidatesQueryOptions,
  emulationErrorMessage,
  startEmulation,
} from '@/queries/emulation.ts';

export function EmulateUserDialog({
  onClose,
  returnFocusRef,
}: {
  onClose: () => void;
  returnFocusRef: RefObject<HTMLButtonElement | null>;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const router = useRouter();
  const [searchText, setSearchText] = useState('');
  const [submittedQuery, setSubmittedQuery] = useState<string | null>(null);
  const [selectedIamId, setSelectedIamId] = useState<string | null>(null);
  const candidatesQuery = useQuery(
    emulationCandidatesQueryOptions(submittedQuery)
  );
  const candidates = submittedQuery ? candidatesQuery.data : undefined;
  const selectedCandidate = candidates?.find(
    (candidate) =>
      candidate.isActive &&
      candidate.isActiveInIam !== false &&
      (candidate.iamId === selectedIamId ||
        (selectedIamId === null && candidates.length === 1))
  );
  const startMutation = useMutation({
    mutationFn: startEmulation,
    onSuccess: () => router.navigate({ href: '/temp', reloadDocument: true }),
    retry: false,
  });
  const isStarting = startMutation.isPending || startMutation.isSuccess;

  useEffect(() => {
    const dialog = dialogRef.current;
    const previousFocus = returnFocusRef.current;
    dialog?.showModal();
    return () => {
      dialog?.close();
      if (previousFocus?.isConnected) {
        previousFocus.focus();
      }
    };
  }, [returnFocusRef]);

  function searchCandidates(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const query = searchText.trim();
    if (!query || isStarting) {
      return;
    }
    setSelectedIamId(null);
    startMutation.reset();
    if (query === submittedQuery) {
      void candidatesQuery.refetch();
    } else {
      setSubmittedQuery(query);
    }
  }

  function searchAgain() {
    setSubmittedQuery(null);
    setSelectedIamId(null);
    startMutation.reset();
    inputRef.current?.focus();
    inputRef.current?.select();
  }

  const canStart =
    !!selectedCandidate &&
    !candidatesQuery.isFetching &&
    !candidatesQuery.isError &&
    !isStarting;

  return (
    <dialog
      aria-describedby="emulate-user-description"
      aria-labelledby="emulate-user-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!isStarting) {
          onClose();
        }
      }}
      ref={dialogRef}
    >
      <div className="modal-box max-w-2xl">
        <h2
          className="text-2xl font-semibold text-primary"
          id="emulate-user-title"
        >
          Emulate user
        </h2>
        <p className="mt-2 text-base-content/70" id="emulate-user-description">
          Find a person and use Booking with their access. You can return to
          your account from the user menu.
        </p>

        <form className="mt-6" onSubmit={searchCandidates}>
          <label
            className="mb-2 block text-sm font-semibold"
            htmlFor="emulation-person-search"
          >
            Email, IAM ID, or Kerberos ID
          </label>
          <div className="flex flex-col gap-3 sm:flex-row">
            <input
              autoComplete="off"
              className="input input-bordered w-full"
              disabled={isStarting}
              id="emulation-person-search"
              maxLength={128}
              onChange={(event) => {
                setSearchText(event.target.value);
                setSubmittedQuery(null);
                setSelectedIamId(null);
                startMutation.reset();
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
                !searchText.trim() || candidatesQuery.isFetching || isStarting
              }
              type="submit"
            >
              <MagnifyingGlassIcon aria-hidden="true" className="h-4 w-4" />
              {candidatesQuery.isFetching ? 'Searching…' : 'Search'}
            </button>
          </div>
          <p className="mt-2 text-sm text-base-content/60">
            Use the exact email address or ID. Names and partial matches are not
            searched.
          </p>
        </form>

        <div aria-live="polite" className="mt-5">
          {submittedQuery && candidatesQuery.isFetching && (
            <p className="text-base-content/70" role="status">
              Searching for a matching person…
            </p>
          )}
          {submittedQuery && candidatesQuery.isError && (
            <p className="alert alert-error" role="alert">
              {emulationErrorMessage(
                candidatesQuery.error,
                'We could not search for that person. Please try again.'
              )}
            </p>
          )}
          {candidates &&
            !candidatesQuery.isFetching &&
            !candidatesQuery.isError &&
            (candidates.length === 0 ? (
              <p className="rounded-lg bg-base-200 p-4" role="status">
                No people matched that email, IAM ID, or Kerberos ID.
              </p>
            ) : (
              <fieldset>
                <legend className="mb-3 font-semibold">
                  {candidates.length === 1
                    ? 'Review the matching person'
                    : 'Select a matching person'}
                </legend>
                {candidates.length === 10 && (
                  <p className="mb-3 text-sm text-base-content/70">
                    Showing the first 10 matches. Search by IAM ID or Kerberos
                    ID to narrow the results.
                  </p>
                )}
                <div className="space-y-3">
                  {candidates.map((candidate) => (
                    <label
                      className={`flex gap-3 rounded-xl border p-4 ${selectedCandidate?.iamId === candidate.iamId ? 'border-primary bg-primary/5' : 'border-base-300'}`}
                      key={candidate.iamId}
                    >
                      <input
                        aria-label={candidate.name}
                        checked={selectedCandidate?.iamId === candidate.iamId}
                        className="radio radio-primary mt-1 shrink-0"
                        disabled={
                          !candidate.isActive ||
                          candidate.isActiveInIam === false ||
                          isStarting
                        }
                        name="emulation-person"
                        onChange={() => {
                          setSelectedIamId(candidate.iamId);
                          startMutation.reset();
                        }}
                        type="radio"
                        value={candidate.iamId}
                      />
                      <div className="min-w-0">
                        <p className="font-semibold">{candidate.name}</p>
                        <dl className="mt-2 grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
                          <dt className="text-base-content/60">Email</dt>
                          <dd className="break-all">
                            {candidate.email || 'Not available'}
                          </dd>
                          <dt className="text-base-content/60">IAM ID</dt>
                          <dd>{candidate.iamId}</dd>
                          <dt className="text-base-content/60">Kerberos ID</dt>
                          <dd>{candidate.kerberos || 'Not available'}</dd>
                          {candidate.isActiveInIam !== null && (
                            <>
                              <dt className="text-base-content/60">
                                IAM status
                              </dt>
                              <dd>
                                {candidate.isActiveInIam
                                  ? 'Active'
                                  : 'Inactive'}
                              </dd>
                            </>
                          )}
                        </dl>
                        {!candidate.isActive && (
                          <p className="mt-3 text-sm text-error">
                            This user is inactive and cannot be emulated.
                          </p>
                        )}
                        {candidate.isActiveInIam === false && (
                          <p className="mt-3 text-sm text-error">
                            This person is inactive in IAM and cannot be
                            emulated.
                          </p>
                        )}
                      </div>
                    </label>
                  ))}
                </div>
                {selectedCandidate && (
                  <p className="mt-4 text-sm text-base-content/70">
                    You will use Booking as {selectedCandidate.name}.
                    {!selectedCandidate.hasUserAccount &&
                      ' A Booking account will be created for this person.'}
                  </p>
                )}
              </fieldset>
            ))}
        </div>

        {startMutation.isError && (
          <p className="alert alert-error mt-4" role="alert">
            {emulationErrorMessage(
              startMutation.error,
              'We could not start emulating this user. Search again and retry.'
            )}
          </p>
        )}
        <div className="modal-action flex-wrap">
          <button
            className="btn btn-ghost"
            disabled={isStarting}
            onClick={onClose}
            type="button"
          >
            Cancel
          </button>
          {submittedQuery && (
            <button
              className="btn btn-outline"
              disabled={isStarting}
              onClick={searchAgain}
              type="button"
            >
              Search again
            </button>
          )}
          <button
            className="btn btn-primary"
            disabled={!canStart}
            onClick={() => {
              if (canStart && selectedCandidate) {
                startMutation.mutate(selectedCandidate.iamId);
              }
            }}
            type="button"
          >
            {isStarting ? 'Starting…' : 'Start emulating'}
          </button>
        </div>
      </div>
    </dialog>
  );
}
