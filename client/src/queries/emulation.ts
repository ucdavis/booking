import { queryOptions } from '@tanstack/react-query';
import type { EmulationCandidate } from '@/features/admin/models/EmulationCandidate.ts';
import { fetchJson, HttpError } from '@/lib/api.ts';

export const emulationIdentityStorageKey = 'booking.emulation.identity-changed';

export function subscribeToEmulationChanges(onChange: () => void) {
  const handleStorageChange = (event: StorageEvent) => {
    if (
      event.key === emulationIdentityStorageKey &&
      event.newValue !== null &&
      event.storageArea === window.localStorage
    ) {
      onChange();
    }
  };
  window.addEventListener('storage', handleStorageChange);
  return () => window.removeEventListener('storage', handleStorageChange);
}

export const emulationCandidatesQueryOptions = (query: string | null) =>
  queryOptions({
    enabled: !!query,
    gcTime: 0,
    queryFn: ({ signal }) =>
      fetchJson<EmulationCandidate[]>(
        `/api/emulation/candidates?query=${encodeURIComponent(query ?? '')}`,
        { cache: 'no-store', skipRedirectOn401: true },
        signal
      ),
    queryKey: ['emulation', 'candidates', query] as const,
    retry: false,
    staleTime: 0,
  });

async function emulationRequest(path: string, body?: string): Promise<void> {
  const { token } = await fetchJson<{ token: string }>(
    '/api/emulation/antiforgery',
    { cache: 'no-store', skipRedirectOn401: true }
  );

  await fetchJson<void>(`/api/emulation/${path}`, {
    body,
    cache: 'no-store',
    headers: { RequestVerificationToken: token },
    method: 'POST',
    skipRedirectOn401: true,
  });

  try {
    // A storage event reaches other tabs without reloading the sending tab twice.
    window.localStorage.setItem(
      emulationIdentityStorageKey,
      crypto.randomUUID()
    );
  } catch {
    // A storage restriction must not prevent this tab from completing its reload.
  }
}

export const startEmulation = (iamId: string) =>
  emulationRequest('start', JSON.stringify({ iamId }));

export const stopEmulation = () => emulationRequest('stop');

export function emulationErrorMessage(error: unknown, fallback: string) {
  if (
    error instanceof HttpError &&
    error.status >= 400 &&
    error.status < 500 &&
    typeof error.body === 'string'
  ) {
    const message = error.body.trim();
    if (message.length > 0 && message.length <= 300 && !message.includes('<')) {
      return message;
    }
  }
  return fallback;
}
