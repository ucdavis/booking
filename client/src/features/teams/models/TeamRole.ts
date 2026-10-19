export enum TeamRole {
  Admin = 'admin',
  Editor = 'editor',
  Viewer = 'viewer',
}

export const assignableTeamRoles = [TeamRole.Admin, TeamRole.Editor] as const;

export const teamRoleLabels: Record<TeamRole, string> = {
  [TeamRole.Admin]: 'Admin',
  [TeamRole.Editor]: 'Editor',
  [TeamRole.Viewer]: 'Viewer',
};

export function createTeamRoleSelection(roles: TeamRole[] = []) {
  return {
    admin: roles.includes(TeamRole.Admin),
    editor: roles.includes(TeamRole.Editor),
    viewer: roles.includes(TeamRole.Viewer),
  };
}

export function selectedTeamRoles(selection: Record<TeamRole, boolean>) {
  return Object.values(TeamRole).filter((role) => selection[role]);
}
