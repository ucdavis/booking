import type { TeamRole } from './TeamRole.ts';

export interface TeamSummary {
  id: number;
  name: string;
  /** The current user's roles when returned by the memberships endpoint. */
  roles: TeamRole[];
  slug: string;
}
