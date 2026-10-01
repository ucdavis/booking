export interface SaveResourceTemplateRequest {
  description?: string | null;
  formJson: string;
  formSchemaVersion: number;
  isActive: boolean;
  name: string;
  updatedAt?: string;
}
