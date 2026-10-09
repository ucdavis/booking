export interface TeamPaymentsSettings {
  maskedApiKey: string | null;
  message: string | null;
  paymentsTeamName: string | null;
  paymentsTeamSlug: string | null;
  status: 'unconfigured' | 'valid' | 'invalid' | 'unavailable';
}
