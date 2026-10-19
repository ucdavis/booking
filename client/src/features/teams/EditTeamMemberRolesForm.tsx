import { useStore } from '@tanstack/react-form';
import type { TeamMember } from './models/TeamMember.ts';
import {
  createTeamRoleSelection,
  selectedTeamRoles,
  TeamRole,
  teamRoleLabels,
} from './models/TeamRole.ts';
import { TeamRoleFields } from './TeamRoleFields.tsx';
import { useAppForm } from '@/shared/forms/formContext.tsx';

export function EditTeamMemberRolesForm({
  hasError,
  isPending,
  member,
  onCancel,
  onChange,
  onSave,
}: {
  hasError: boolean;
  isPending: boolean;
  member: TeamMember;
  onCancel: () => void;
  onChange: () => void;
  onSave: (roles: TeamRole[]) => Promise<void>;
}) {
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
      try {
        await onSave(roles);
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

  return (
    <form
      aria-labelledby="manage-team-roles-heading"
      className="mt-6 rounded-sm border border-base-300 bg-base-100 p-5"
      onSubmit={(event) => {
        event.preventDefault();
        event.stopPropagation();
        void form.handleSubmit();
      }}
    >
      <h3 className="font-semibold" id="manage-team-roles-heading">
        Manage team roles
      </h3>
      <p className="mt-2 text-base-content/80">
        Choose roles for {member.name}. Their current roles are{' '}
        {member.roles.map((role) => teamRoleLabels[role]).join(', ')}.
      </p>
      <div className="mt-4">
        <TeamRoleFields
          disabled={pending}
          error={roles.length === 0 ? 'Select at least one role.' : undefined}
          fields="roles"
          form={form}
          includeViewer={member.roles.includes(TeamRole.Viewer)}
          onChange={onChange}
        />
      </div>
      <p className="mt-3 text-sm text-base-content/65">
        To remove every role, cancel and use Remove member.
      </p>
      {hasError && (
        <p className="mt-3 text-error" role="alert">
          We could not change this member&apos;s roles. Refresh the page to
          check their current access, then try again.
        </p>
      )}
      <div className="mt-4 flex flex-wrap gap-3">
        <button
          className="btn btn-primary btn-sm"
          disabled={pending || roles.length === 0 || unchanged}
          type="submit"
        >
          {pending ? 'Saving…' : 'Save roles'}
        </button>
        <button
          className="btn btn-ghost btn-sm"
          disabled={pending}
          onClick={onCancel}
          type="button"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
