import type { TeamSummary } from './TeamSummary.ts';

export interface TeamAccess {
  isSiteAdmin: boolean;
  role: 'admin' | 'editor' | 'viewer' | null;
  team: TeamSummary;
}
