export interface AdminPerson {
  email: string | null;
  iamId: string;
  isActive: boolean;
  isAdmin: boolean;
  kerberos: string | null;
  name: string;
}
