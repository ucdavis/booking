export interface ResourceTemplate {
  createdAt: string;
  description: string | null;
  formJson: string;
  formSchemaVersion: number;
  id: number;
  isActive: boolean;
  name: string;
  resourceDefaultsJson: string | null;
  teamId: number | null;
  updatedAt: string;
}
