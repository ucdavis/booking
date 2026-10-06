export interface SaveResourceTemplateRequest {
  description?: string | null;
  formJson: string;
  formSchemaVersion: number;
  isActive: boolean;
  name: string;
  resourceDefaultsJson?: string | null;
  updatedAt?: string;
}
