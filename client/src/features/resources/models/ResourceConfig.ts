export interface ResourceConfig {
  detailsJson: string;
  detailsSchemaVersion: number;
  formJson: string;
  formSchemaVersion: number;
  id: number;
  publishedAt: string | null;
  sourceTemplateId: number | null;
  version: number;
}
