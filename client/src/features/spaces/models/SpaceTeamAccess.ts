import type { TeamSummary } from '@/features/teams/models/TeamSummary.ts';

export interface SpaceTeamAccess {
  resourceCount: number;
  team: TeamSummary;
}
