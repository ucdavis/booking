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
