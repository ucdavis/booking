export interface AdminPerson {
  email: string | null;
  iamId: string;
  isActive: boolean;
  isActiveInIam: boolean | null;
  isAdmin: boolean;
  kerberos: string | null;
  name: string;
}
