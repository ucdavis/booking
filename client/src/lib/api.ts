export class HttpError extends Error {
  constructor(
    public status: number,
    public url: string,
    public body?: unknown
  ) {
    super(`HTTP ${status} for ${url}`);
  }
}

const toRedirectParam = () =>
  encodeURIComponent(window.location.pathname + window.location.search);

let csrfTokenPromise: Promise<string> | undefined;

export function clearCsrfToken() {
  csrfTokenPromise = undefined;
}

function getCsrfToken(refresh: boolean): Promise<string> {
  if (!csrfTokenPromise || refresh) {
    const tokenPromise = fetchJson<{ requestToken: string }>(
      '/api/antiforgery',
      {
        cache: 'no-store',
        skipRedirectOn401: true,
      }
    )
      .then(({ requestToken }) => requestToken)
      .catch((error: unknown) => {
        if (csrfTokenPromise === tokenPromise) {
          clearCsrfToken();
        }
        throw error;
      });
    csrfTokenPromise = tokenPromise;
  }
  return csrfTokenPromise;
}

function waitForCsrfToken(
  promise: Promise<string>,
  signal?: AbortSignal | null
) {
  if (!signal) {
    return promise;
  }
  return new Promise<string>((resolve, reject) => {
    signal.throwIfAborted();
    const abort = () => reject(signal.reason);
    signal.addEventListener('abort', abort, { once: true });
    void promise.then(resolve, reject).finally(() => {
      signal.removeEventListener('abort', abort);
    });
  });
}

function redirectToLogin<T>(): Promise<T> {
  window.location.href = `/login?returnUrl=${toRedirectParam()}`;
  // Halt the current render/update while the browser navigates to sign-in.
  return new Promise<T>(() => {});
}

// main fn for fetching json w/ built in error handling, auth redirection and it'll work well w/ react query
export async function fetchJson<T>(
  url: string,
  init: RequestInit & {
    refreshCsrfToken?: boolean;
    skipRedirectOn401?: boolean;
  } = {},
  signal?: AbortSignal
): Promise<T> {
  const {
    headers: customHeaders,
    refreshCsrfToken = false,
    skipRedirectOn401,
    ...requestInit
  } = init;
  const headers = new Headers(customHeaders);
  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }
  if (requestInit.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const method = (requestInit.method ?? 'GET').toUpperCase();
  const isSameOrigin =
    new URL(url, window.location.href).origin === window.location.origin;
  let requestTokenPromise: Promise<string> | undefined;
  if (
    isSameOrigin &&
    !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(method) &&
    !headers.has('RequestVerificationToken')
  ) {
    (requestInit.signal ?? signal)?.throwIfAborted();
    try {
      // Tokens belong to the effective identity. Recovery requests must refresh
      // because emulation can become unavailable without a document reload.
      requestTokenPromise = getCsrfToken(refreshCsrfToken);
      headers.set(
        'RequestVerificationToken',
        await waitForCsrfToken(
          requestTokenPromise,
          requestInit.signal ?? signal
        )
      );
    } catch (error) {
      if (
        error instanceof HttpError &&
        error.status === 401 &&
        !skipRedirectOn401
      ) {
        return redirectToLogin<T>();
      }
      throw error;
    }
    (requestInit.signal ?? signal)?.throwIfAborted();
  }

  const res = await fetch(url, {
    credentials: 'same-origin', // front/back proxy on same domain and during prod it's same origin too
    signal, // for cancellation/abort
    ...requestInit,
    headers,
  });

  if (
    requestTokenPromise &&
    requestTokenPromise === csrfTokenPromise &&
    [400, 401, 403].includes(res.status)
  ) {
    // Discard potentially stale tokens, but never replay a mutation automatically.
    clearCsrfToken();
  }

  // 204 No Content
  if (res.status === 204) {
    return undefined as T;
  }

  // Auto-redirect on 401
  if (res.status === 401 && !skipRedirectOn401) {
    return redirectToLogin<T>();
  }

  const text = await res.text();
  const contentType = res.headers.get('content-type') || '';
  const data =
    contentType.includes('application/json') && text ? JSON.parse(text) : text;

  if (!res.ok) {
    throw new HttpError(res.status, url, data);
  }
  return data as T;
}
