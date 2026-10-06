import type { TeamRole } from './TeamRole.ts';

export interface TeamPerson {
  email: string | null;
  iamId: string;
  isActive: boolean;
  isActiveInIam: boolean | null;
  kerberos: string | null;
  name: string;
  role: TeamRole | null;
}
