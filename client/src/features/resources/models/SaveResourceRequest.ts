import type { NewResourceSpace } from './NewResourceSpace.ts';
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
  newSpace?: NewResourceSpace | null;
  publicAccess: ResourceDetail['publicAccess'];
  sourceTemplateId?: number | null;
  spaceId: number | null;
  updatedAt?: string;
}
