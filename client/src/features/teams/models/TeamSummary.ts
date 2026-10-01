import type { TeamRole } from './TeamRole.ts';

export interface TeamSummary {
  id: number;
  name: string;
  /** The current user's role when returned by the memberships endpoint. */
  role?: TeamRole;
  slug: string;
}
