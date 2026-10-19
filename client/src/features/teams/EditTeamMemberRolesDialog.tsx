import { useStore } from '@tanstack/react-form';
import { useEffect, useRef, useState } from 'react';
import type { TeamMember } from './models/TeamMember.ts';
import {
  createTeamRoleSelection,
  selectedTeamRoles,
  TeamRole,
  teamRoleLabels,
} from './models/TeamRole.ts';
import { TeamRoleFields } from './TeamRoleFields.tsx';
import { useAppForm } from '@/shared/forms/formContext.tsx';

export function EditTeamMemberRolesDialog({
  hasError,
  isCurrentUser,
  isPending,
  isSiteAdmin,
  member,
  onChange,
  onClose,
  onSave,
}: {
  hasError: boolean;
  isCurrentUser: boolean;
  isPending: boolean;
  isSiteAdmin: boolean;
  member: TeamMember;
  onChange: () => void;
  onClose: () => void;
  onSave: (
    roles: TeamRole[],
    confirmSelfAccessChange: boolean
  ) => Promise<void>;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const confirmationRef = useRef<HTMLHeadingElement>(null);
  const [isConfirming, setIsConfirming] = useState(false);
  const form = useAppForm({
    defaultValues: { roles: createTeamRoleSelection(member.roles) },
    onSubmit: async ({ value }) => {
      const roles = selectedTeamRoles(value.roles);
      if (
        isPending ||
        roles.length === 0 ||
        (roles.length === member.roles.length &&
          roles.every((role) => member.roles.includes(role)))
      ) {
        return;
      }
      const needsConfirmation =
        isCurrentUser &&
        member.roles.includes(TeamRole.Admin) &&
        !roles.includes(TeamRole.Admin);
      if (needsConfirmation && !isConfirming) {
        setIsConfirming(true);
        return;
      }
      try {
        await onSave(roles, needsConfirmation && isConfirming);
      } catch {
        // Retain the selected roles after a failed save.
      }
    },
  });
  const selection = useStore(form.store, (state) => state.values.roles);
  const isSubmitting = useStore(form.store, (state) => state.isSubmitting);
  const roles = selectedTeamRoles(selection);
  const pending = isPending || isSubmitting;
  const unchanged =
    roles.length === member.roles.length &&
    roles.every((role) => member.roles.includes(role));

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
    if (isConfirming) {
      confirmationRef.current?.focus();
    } else {
      dialogRef.current?.querySelector<HTMLInputElement>('input')?.focus();
    }
  }, [isConfirming]);

  return (
    <dialog
      aria-describedby="manage-team-roles-description"
      aria-labelledby="manage-team-roles-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!pending) {
          onClose();
        }
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget && !pending) {
          onClose();
        }
      }}
      ref={dialogRef}
    >
      <div className="modal-box max-w-2xl">
        <h2
          className="text-2xl font-semibold text-primary"
          id="manage-team-roles-title"
        >
          Manage team roles
        </h2>
        <p
          className="mt-2 text-base-content/80"
          id="manage-team-roles-description"
        >
          Choose roles for {member.name}. Their current roles are{' '}
          {member.roles.map((role) => teamRoleLabels[role]).join(', ')}.
        </p>
        <form
          className="mt-6"
          onSubmit={(event) => {
            event.preventDefault();
            event.stopPropagation();
            void form.handleSubmit();
          }}
        >
          <div hidden={isConfirming}>
            <TeamRoleFields
              disabled={pending || isConfirming}
              error={
                roles.length === 0 ? 'Select at least one role.' : undefined
              }
              fields="roles"
              form={form}
              includeViewer={member.roles.includes(TeamRole.Viewer)}
              onChange={onChange}
            />
            <p className="mt-3 text-sm text-base-content/65">
              To remove every role, cancel and use Remove member.
            </p>
          </div>
          {isConfirming && (
            <section className="rounded-sm border border-warning/40 bg-warning/10 p-4">
              <h3 className="font-semibold" ref={confirmationRef} tabIndex={-1}>
                Remove your Admin role?
              </h3>
              <p className="mt-2">
                {isSiteAdmin
                  ? 'You will keep access to manage this team through site administration.'
                  : 'You will no longer be able to manage team members or payment settings. You will be taken to the team overview.'}
              </p>
              <p className="mt-2 text-sm">
                Your remaining roles:{' '}
                {roles.map((role) => teamRoleLabels[role]).join(', ')}.
              </p>
            </section>
          )}
          {hasError && (
            <p className="mt-4 text-error" role="alert">
              We could not change this member&apos;s roles. Refresh the page to
              check their current access, then try again.
            </p>
          )}
          <div className="modal-action flex-wrap">
            <button
              className="btn btn-ghost"
              disabled={pending}
              onClick={onClose}
              type="button"
            >
              Cancel
            </button>
            {isConfirming && (
              <button
                className="btn btn-outline"
                disabled={pending}
                onClick={() => {
                  setIsConfirming(false);
                  onChange();
                }}
                type="button"
              >
                Back to roles
              </button>
            )}
            <button
              className={`btn ${isConfirming ? 'btn-warning' : 'btn-primary'}`}
              disabled={pending || roles.length === 0 || unchanged}
              type="submit"
            >
              {pending
                ? 'Saving…'
                : isConfirming
                  ? 'Confirm role change'
                  : 'Save roles'}
            </button>
          </div>
        </form>
      </div>
    </dialog>
  );
}
