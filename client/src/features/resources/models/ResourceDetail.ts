import type { ResourceConfig } from './ResourceConfig.ts';
import type { ResourceSummary } from './ResourceSummary.ts';

export interface ResourceDetail extends ResourceSummary {
  allowReservationSharing: boolean;
  approvalMode: 'automatic' | 'manual';
  config: ResourceConfig | null;
  createdAt: string;
  description: string | null;
  followTeamCalendar: boolean;
  publicAccess: 'none' | 'availability' | 'details' | null;
}
