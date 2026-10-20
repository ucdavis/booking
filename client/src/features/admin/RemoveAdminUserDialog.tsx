import { useEffect, useRef } from 'react';
import type { AdminUser } from './models/AdminUser.ts';

export function RemoveAdminUserDialog({
  errorMessage,
  isPending,
  onClose,
  onConfirm,
  user,
}: {
  errorMessage: string | null;
  isPending: boolean;
  onClose: () => void;
  onConfirm: () => void;
  user: AdminUser;
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
      aria-describedby="remove-admin-description"
      aria-labelledby="remove-admin-title"
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
          id="remove-admin-title"
        >
          Remove admin access?
        </h2>
        <p className="mt-3 text-base-content/80" id="remove-admin-description">
          {user.name} will no longer be a site admin. Their user record and
          other access will remain.
        </p>
        {errorMessage && (
          <p className="mt-4 text-error" role="alert">
            {errorMessage}
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
