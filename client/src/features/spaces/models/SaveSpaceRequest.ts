export interface SaveSpaceRequest {
  address: string | null;
  description: string | null;
  isActive: boolean;
  isOfficialFacility: boolean;
  name: string;
  referenceNumber: string | null;
  slug: string;
}
