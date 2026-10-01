import type { TeamRole } from './TeamRole.ts';

export interface TeamPerson {
  email: string | null;
  iamId: string;
  isActive: boolean;
  isActiveInIam: boolean;
  kerberos: string | null;
  name: string;
  role: TeamRole | null;
}
