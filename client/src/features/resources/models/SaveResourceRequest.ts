import type { ResourceDetail } from './ResourceDetail.ts';

export interface SaveResourceRequest {
  allowReservationSharing: boolean;
  approvalMode: ResourceDetail['approvalMode'];
  capacity: number | null;
  category: string;
  description: string | null;
  detailsJson?: string;
  detailsSchemaVersion?: number;
  expectedConfigId?: number | null;
  followTeamCalendar: boolean;
  formJson?: string;
  formSchemaVersion?: number;
  isActive: boolean;
  isReservable: boolean;
  name: string;
  publicAccess: ResourceDetail['publicAccess'];
  slug: string;
  sourceTemplateId?: number | null;
  spaceId: number;
  updatedAt?: string;
}
