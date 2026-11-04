export interface Space {
  address: string | null;
  description: string | null;
  id: number;
  isActive: boolean;
  isOfficialFacility: boolean;
  name: string;
  referenceNumber: string | null;
  slug: string;
  /** Attached teams, populated by the site admin spaces list. */
  teams?: { id: number; name: string; slug: string }[];
}
