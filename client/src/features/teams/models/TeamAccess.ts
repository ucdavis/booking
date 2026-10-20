import type { TeamRole } from './TeamRole.ts';
import type { TeamSummary } from './TeamSummary.ts';

export interface TeamAccess {
  isSiteAdmin: boolean;
  roles: TeamRole[];
  team: TeamSummary;
}
