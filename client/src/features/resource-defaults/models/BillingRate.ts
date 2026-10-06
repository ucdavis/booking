export interface BillingRate {
  [key: string]: unknown;
  // A decimal amount in US dollars, kept as text to preserve precision.
  amount: string;
  basis: 'hour' | 'day' | 'booking';
  id: string;
  name: string;
}
