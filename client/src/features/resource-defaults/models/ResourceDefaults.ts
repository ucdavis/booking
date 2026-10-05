import type { BillingRate } from './BillingRate.ts';
import type { OpeningHoursInterval } from './OpeningHoursInterval.ts';

export interface ResourceDefaults {
  [key: string]: unknown;
  billingRates?: BillingRate[];
  openingHours?: Partial<
    Record<
      | 'monday'
      | 'tuesday'
      | 'wednesday'
      | 'thursday'
      | 'friday'
      | 'saturday'
      | 'sunday',
      OpeningHoursInterval[]
    >
  > &
    Record<string, unknown>;
  schemaVersion?: 1;
}
