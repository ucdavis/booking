import { useEffect, useRef } from 'react';
import type { TeamMember } from './models/TeamMember.ts';

export function RemoveTeamMemberDialog({
  hasError,
  isCurrentUser,
  isPending,
  isSiteAdmin,
  member,
  onClose,
  onConfirm,
}: {
  hasError: boolean;
  isCurrentUser: boolean;
  isPending: boolean;
  isSiteAdmin: boolean;
  member: TeamMember;
  onClose: () => void;
  onConfirm: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    const previousFocus = document.activeElement as HTMLElement | null;
    dialog?.showModal();
    cancelRef.current?.focus();
    return () => {
      dialog?.close();
      if (previousFocus?.isConnected) {
        previousFocus.focus?.();
      }
    };
  }, []);

  return (
    <dialog
      aria-describedby="remove-team-member-description"
      aria-labelledby="remove-team-member-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!isPending) {
          onClose();
        }
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget && !isPending) {
          onClose();
        }
      }}
      ref={dialogRef}
    >
      <div className="modal-box max-w-2xl">
        <h2
          className="text-2xl font-semibold text-primary"
          id="remove-team-member-title"
        >
          Remove team member?
        </h2>
        <p
          className="mt-3 text-base-content/80"
          id="remove-team-member-description"
        >
          Remove all of {member.name}&apos;s roles on this team. Their user
          account, other teams, and any site admin access will remain.
        </p>
        {isCurrentUser && (
          <div className="mt-5 rounded-sm border border-warning/40 bg-warning/10 p-4">
            <p className="font-semibold">
              You are removing all of your team roles.
            </p>
            <p className="mt-2">
              {isSiteAdmin
                ? 'You will keep access through site administration.'
                : 'You will lose access to this team and be taken to your profile.'}
            </p>
          </div>
        )}
        {hasError && (
          <p className="mt-4 text-error" role="alert">
            We could not remove this team member. Refresh the page to check
            their current access, then try again.
          </p>
        )}
        <div className="modal-action">
          <button
            className="btn btn-ghost"
            disabled={isPending}
            onClick={onClose}
            ref={cancelRef}
            type="button"
          >
            Cancel
          </button>
          <button
            className="btn btn-error"
            disabled={isPending}
            onClick={onConfirm}
            type="button"
          >
            {isPending ? 'Removing…' : 'Confirm removal'}
          </button>
        </div>
      </div>
    </dialog>
  );
}
