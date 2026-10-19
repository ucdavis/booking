export enum TeamRole {
  Admin = 'admin',
  Approver = 'approver',
  Editor = 'editor',
  Viewer = 'viewer',
}

export const assignableTeamRoles = [
  TeamRole.Admin,
  TeamRole.Editor,
  TeamRole.Approver,
] as const;

export const teamRoleLabels: Record<TeamRole, string> = {
  [TeamRole.Admin]: 'Admin',
  [TeamRole.Approver]: 'Approver',
  [TeamRole.Editor]: 'Editor',
  [TeamRole.Viewer]: 'Viewer',
};

export function createTeamRoleSelection(roles: TeamRole[] = []) {
  return {
    admin: roles.includes(TeamRole.Admin),
    approver: roles.includes(TeamRole.Approver),
    editor: roles.includes(TeamRole.Editor),
    viewer: roles.includes(TeamRole.Viewer),
  };
}

export function selectedTeamRoles(selection: Record<TeamRole, boolean>) {
  return Object.values(TeamRole).filter((role) => selection[role]);
}
