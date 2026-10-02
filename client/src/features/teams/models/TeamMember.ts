import type { TeamRole } from './TeamRole.ts';

export interface TeamMember {
  email: string | null;
  iamId: string;
  id: number;
  isActive: boolean;
  name: string;
  role: TeamRole;
}
