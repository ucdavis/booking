import { useId } from 'react';
import {
  assignableTeamRoles,
  createTeamRoleSelection,
  TeamRole,
  teamRoleLabels,
} from './models/TeamRole.ts';
import { withFieldGroup } from '@/shared/forms/formContext.tsx';

export const TeamRoleFields = withFieldGroup({
  defaultValues: createTeamRoleSelection(),
  props: {
    disabled: false,
    error: undefined as string | undefined,
    includeViewer: false,
    onChange: undefined as (() => void) | undefined,
  },
  render: function TeamRoleFields({
    disabled,
    error,
    group,
    includeViewer,
    onChange,
  }) {
    const prefix = useId();
    const roles = includeViewer
      ? [...assignableTeamRoles, TeamRole.Viewer]
      : assignableTeamRoles;
    const descriptions: Record<TeamRole, string> = {
      admin: 'Manage this team and its members.',
      editor: 'Manage team resource templates without managing members.',
      viewer: 'Existing view access. This role cannot be newly assigned.',
    };

    return (
      <fieldset
        aria-describedby={error ? `${prefix}-error` : undefined}
        className="space-y-3"
        disabled={disabled}
      >
        <legend className="mb-3 text-sm font-semibold">Team roles</legend>
        {roles.map((role) => (
          <group.Field key={role} name={role}>
            {(field) => (
              <div className="flex items-start gap-3">
                <input
                  aria-describedby={`${prefix}-${role}-help${error ? ` ${prefix}-error` : ''}`}
                  aria-invalid={!!error || undefined}
                  checked={field.state.value}
                  className="checkbox checkbox-primary mt-0.5"
                  id={`${prefix}-${role}`}
                  name={field.name}
                  onBlur={field.handleBlur}
                  onChange={(event) => {
                    field.handleChange(event.target.checked);
                    onChange?.();
                  }}
                  type="checkbox"
                />
                <div>
                  <label className="font-medium" htmlFor={`${prefix}-${role}`}>
                    {teamRoleLabels[role]}
                  </label>
                  <p
                    className="mt-1 text-sm text-base-content/65"
                    id={`${prefix}-${role}-help`}
                  >
                    {descriptions[role]}
                  </p>
                </div>
              </div>
            )}
          </group.Field>
        ))}
        {error && (
          <p className="text-sm text-error" id={`${prefix}-error`} role="alert">
            {error}
          </p>
        )}
      </fieldset>
    );
  },
});
