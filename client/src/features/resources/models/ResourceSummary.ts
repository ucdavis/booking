export interface ResourceSummary {
  capacity: number | null;
  category: string;
  id: number;
  isActive: boolean;
  isReservable: boolean;
  name: string;
  parentResourceId: number | null;
  slug: string;
  spaceId: number;
  spaceName: string;
  teamId: number;
  updatedAt: string;
}
