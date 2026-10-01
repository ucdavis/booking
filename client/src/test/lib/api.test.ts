import { afterEach, describe, expect, it, vi } from 'vitest';
import { clearCsrfToken, fetchJson, HttpError } from '@/lib/api.ts';

afterEach(() => {
  clearCsrfToken();
  vi.unstubAllGlobals();
});

describe('fetchJson', () => {
  it.each([
    { 'X-Request-Id': 'example' },
    [['X-Request-Id', 'example']],
    new Headers({ 'X-Request-Id': 'example' }),
  ] satisfies HeadersInit[])(
    'keeps JSON defaults when custom headers are supplied: %j',
    async (headers) => {
      const fetchMock = vi
        .fn()
        .mockResolvedValueOnce(Response.json({ requestToken: 'csrf-token' }))
        .mockResolvedValueOnce(Response.json({}));
      vi.stubGlobal('fetch', fetchMock);

      await fetchJson('/api/example', { body: '{}', headers, method: 'POST' });

      const request = fetchMock.mock.calls[1][1] as RequestInit;
      const sentHeaders = new Headers(request.headers);
      expect(sentHeaders.get('Accept')).toBe('application/json');
      expect(sentHeaders.get('Content-Type')).toBe('application/json');
      expect(sentHeaders.get('X-Request-Id')).toBe('example');
      expect(sentHeaders.get('RequestVerificationToken')).toBe('csrf-token');
      expect(request.credentials).toBe('same-origin');
    }
  );

  it('honors explicit header overrides and preserves cancellation', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf-token' }))
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    await fetchJson('/api/example', {
      body: 'text',
      headers: { accept: 'text/plain', 'content-type': 'text/plain' },
      method: 'POST',
      signal: controller.signal,
      skipRedirectOn401: true,
    });

    const request = fetchMock.mock.calls[1][1] as RequestInit;
    expect(new Headers(request.headers).get('Accept')).toBe('text/plain');
    expect(new Headers(request.headers).get('Content-Type')).toBe('text/plain');
    expect(request.signal).toBe(controller.signal);
    expect(request).not.toHaveProperty('skipRedirectOn401');
    expect(request).not.toHaveProperty('refreshCsrfToken');
  });

  it.each(['POST', 'PUT', 'PATCH', 'DELETE', 'post'])(
    'automatically protects same-origin %s requests',
    async (method) => {
      const fetchMock = vi
        .fn()
        .mockResolvedValueOnce(Response.json({ requestToken: 'csrf-token' }))
        .mockResolvedValueOnce(new Response(null, { status: 204 }));
      vi.stubGlobal('fetch', fetchMock);

      await fetchJson('/api/example', { method });

      expect(fetchMock.mock.calls[0][0]).toBe('/api/antiforgery');
      expect(fetchMock.mock.calls[0][1].cache).toBe('no-store');
      expect(
        new Headers(fetchMock.mock.calls[1][1].headers).get(
          'RequestVerificationToken'
        )
      ).toBe('csrf-token');
    }
  );

  it.each(['GET', 'HEAD', 'OPTIONS', 'TRACE'])(
    'does not fetch or attach a token for %s requests',
    async (method) => {
      const fetchMock = vi
        .fn()
        .mockResolvedValue(new Response(null, { status: 204 }));
      vi.stubGlobal('fetch', fetchMock);

      await fetchJson('/api/example', { method });

      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(
        new Headers(fetchMock.mock.calls[0][1].headers).has(
          'RequestVerificationToken'
        )
      ).toBe(false);
    }
  );

  it('does not send the application token to another origin', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await fetchJson('https://other.example.test/api/example', {
      method: 'POST',
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(
      new Headers(fetchMock.mock.calls[0][1].headers).has(
        'RequestVerificationToken'
      )
    ).toBe(false);
  });

  it('preserves an explicitly supplied antiforgery token', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await fetchJson('/api/example', {
      headers: { requestverificationtoken: 'explicit-token' },
      method: 'POST',
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(
      new Headers(fetchMock.mock.calls[0][1].headers).get(
        'RequestVerificationToken'
      )
    ).toBe('explicit-token');
  });

  it('shares an in-flight token request and reuses the result', async () => {
    const tokenResponse = Promise.withResolvers<Response>();
    const fetchMock = vi
      .fn()
      .mockReturnValueOnce(tokenResponse.promise)
      .mockImplementation(() =>
        Promise.resolve(new Response(null, { status: 204 }))
      );
    vi.stubGlobal('fetch', fetchMock);

    const first = fetchJson('/api/first', { method: 'POST' });
    const second = fetchJson('/api/second', { method: 'DELETE' });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    tokenResponse.resolve(Response.json({ requestToken: 'shared-token' }));
    await Promise.all([first, second]);
    await fetchJson('/api/third', { method: 'PUT' });

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      '/api/antiforgery',
      '/api/first',
      '/api/second',
      '/api/third',
    ]);
    for (const [, request] of fetchMock.mock.calls.slice(1)) {
      expect(new Headers(request.headers).get('RequestVerificationToken')).toBe(
        'shared-token'
      );
    }
  });

  it('fetches a fresh token for recovery even when one is cached', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ requestToken: 'old-identity' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(
        Response.json({ requestToken: 'recovery-identity' })
      )
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await fetchJson('/api/example', { method: 'POST' });
    await fetchJson('/api/emulation/stop', {
      method: 'POST',
      refreshCsrfToken: true,
    });

    expect(fetchMock.mock.calls[2][0]).toBe('/api/antiforgery');
    expect(
      new Headers(fetchMock.mock.calls[3][1].headers).get(
        'RequestVerificationToken'
      )
    ).toBe('recovery-identity');
    expect(fetchMock.mock.calls[3][1]).not.toHaveProperty('refreshCsrfToken');
  });

  it('allows a new token request after a failed fetch without sending the mutation', async () => {
    const fetchMock = vi
      .fn()
      .mockRejectedValueOnce(new Error('Network unavailable'))
      .mockResolvedValueOnce(Response.json({ requestToken: 'retry-token' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(fetchJson('/api/example', { method: 'POST' })).rejects.toThrow(
      'Network unavailable'
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await fetchJson('/api/example', { method: 'POST' });

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      '/api/antiforgery',
      '/api/antiforgery',
      '/api/example',
    ]);
  });

  it('does not discard a refreshed token when an older token request fails', async () => {
    const oldTokenResponse = Promise.withResolvers<Response>();
    const fetchMock = vi
      .fn()
      .mockReturnValueOnce(oldTokenResponse.promise)
      .mockResolvedValueOnce(Response.json({ requestToken: 'new-token' }))
      .mockImplementation(() =>
        Promise.resolve(new Response(null, { status: 204 }))
      );
    vi.stubGlobal('fetch', fetchMock);

    const oldRequest = fetchJson('/api/old', {
      method: 'POST',
      skipRedirectOn401: true,
    });
    await fetchJson('/api/recovery', {
      method: 'POST',
      refreshCsrfToken: true,
    });
    oldTokenResponse.resolve(new Response(null, { status: 401 }));
    await expect(oldRequest).rejects.toMatchObject({ status: 401 });
    await fetchJson('/api/next', { method: 'POST' });

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      '/api/antiforgery',
      '/api/antiforgery',
      '/api/recovery',
      '/api/next',
    ]);
    expect(
      new Headers(fetchMock.mock.calls[3][1].headers).get(
        'RequestVerificationToken'
      )
    ).toBe('new-token');
  });

  it('preserves opt-out of login redirection when the token request is unauthorized', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(
      fetchJson('/api/example', { method: 'POST', skipRedirectOn401: true })
    ).rejects.toMatchObject({ status: 401, url: '/api/antiforgery' });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('invalidates a rejected token without automatically replaying the mutation', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ requestToken: 'stale-token' }))
      .mockResolvedValueOnce(new Response(null, { status: 400 }))
      .mockResolvedValueOnce(Response.json({ requestToken: 'new-token' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(
      fetchJson('/api/example', { method: 'POST' })
    ).rejects.toMatchObject({ status: 400 });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    await fetchJson('/api/example', { method: 'POST' });

    expect(fetchMock.mock.calls[2][0]).toBe('/api/antiforgery');
    expect(
      new Headers(fetchMock.mock.calls[3][1].headers).get(
        'RequestVerificationToken'
      )
    ).toBe('new-token');
  });

  it('does not cancel other callers when one shared-token caller aborts', async () => {
    const tokenResponse = Promise.withResolvers<Response>();
    const fetchMock = vi
      .fn()
      .mockReturnValueOnce(tokenResponse.promise)
      .mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    const canceled = fetchJson('/api/canceled', {
      method: 'POST',
      signal: controller.signal,
    });
    const active = fetchJson('/api/active', { method: 'POST' });
    controller.abort();

    await expect(canceled).rejects.toMatchObject({ name: 'AbortError' });
    tokenResponse.resolve(Response.json({ requestToken: 'shared-token' }));
    await active;
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      '/api/antiforgery',
      '/api/active',
    ]);
  });

  it('throws an HTTP error when a caller opts out of login redirection', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 401 }))
    );

    await expect(
      fetchJson('/api/example', { skipRedirectOn401: true })
    ).rejects.toBeInstanceOf(HttpError);
  });
});
