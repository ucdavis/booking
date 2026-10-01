export interface EmulationCandidate {
  email: string | null;
  hasUserAccount: boolean;
  iamId: string;
  isActive: boolean;
  isActiveInIam: boolean | null;
  kerberos: string | null;
  name: string;
}
