// Local HH:mm times on the selected day; closing time must follow opening time.
export interface OpeningHoursInterval {
  [key: string]: unknown;
  end: string;
  start: string;
}
