import { QueryClient } from '@tanstack/react-query';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { meQueryOptions, type User } from '@/queries/user.ts';
import { Route as AdminRoute } from '@/routes/(authenticated)/admin.tsx';
import { Route as AuthenticatedRoute } from '@/routes/(authenticated)/route.tsx';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

const siteAdmin: User = {
  email: 'admin@example.com',
  iamId: '123456789',
  id: 'user-1',
  isSiteAdmin: true,
  name: 'Taylor',
  roles: [],
};

let cleanup: (() => void) | undefined;

afterEach(() => {
  cleanup?.();
  cleanup = undefined;
  vi.restoreAllMocks();
});

function mockUser(user: User = siteAdmin) {
  server.use(http.get('/api/user/me', () => HttpResponse.json(user)));
}

function silenceRouteErrors() {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
}

describe('site administration', () => {
  it('lets a site admin navigate from resource inventory to the admin landing page', async () => {
    mockUser();
    server.use(
      http.get(
        '/api/admin/access',
        () => new HttpResponse(null, { status: 204 })
      )
    );
    const rendered = renderRoute({ initialPath: '/temp/admin/resources' });
    cleanup = rendered.cleanup;

    const adminMenu = await screen.findByRole('button', { name: 'Site admin' });
    expect(adminMenu).toHaveAttribute('aria-expanded', 'false');
    expect(
      screen.getByRole('heading', { name: 'Resource inventory' })
    ).toBeInTheDocument();
    fireEvent.click(adminMenu);
    expect(adminMenu).toHaveAttribute('aria-expanded', 'true');
    const adminLink = screen.getByRole('link', { name: 'Admin home' });
    expect(adminLink).toHaveAttribute('href', '/admin');
    expect(screen.getByRole('link', { name: 'Admin users' })).toHaveAttribute(
      'href',
      '/admin/users'
    );
    fireEvent.click(adminLink);

    expect(
      await screen.findByRole('heading', { name: 'Site administration' })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe('/admin');
    expect(screen.getByRole('button', { name: 'Site admin' })).toHaveAttribute(
      'aria-expanded',
      'false'
    );
    expect(
      screen.getByRole('link', { name: /site admin users/i })
    ).toHaveAttribute('href', '/admin/users');
  });

  it('checks admin access when the landing page is opened directly', async () => {
    mockUser();
    let accessRequests = 0;
    server.use(
      http.get('/api/admin/access', () => {
        accessRequests += 1;
        return new HttpResponse(null, { status: 204 });
      })
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin' }));

    expect(
      await screen.findByRole('heading', { name: 'Site administration' })
    ).toBeInTheDocument();
    expect(accessRequests).toBe(1);
  });

  it.each([
    { roles: [] },
    { roles: ['Admin'] },
    { roles: ['admin'] },
    { roles: ['SiteAdmin'] },
  ])(
    'hides site administration and denies direct access without site admin permission (roles: $roles)',
    async ({ roles }) => {
      silenceRouteErrors();
      mockUser({ ...siteAdmin, isSiteAdmin: false, roles });
      server.use(
        http.get(
          '/api/admin/access',
          () => new HttpResponse(null, { status: 403 })
        )
      );
      const rendered = renderRoute({ initialPath: '/admin' });
      cleanup = rendered.cleanup;

      expect(
        await screen.findByRole('heading', { name: 'Not authorized' })
      ).toBeInTheDocument();
      expect(
        screen.getByText("You don't have permission to view this page.")
      ).toBeInTheDocument();
      expect(
        screen.getByRole('link', { name: 'Back to Grove' })
      ).toHaveAttribute('href', '/temp');
      expect(rendered.router.state.location.pathname).toBe('/admin');
      expect(
        screen.queryByRole('button', { name: 'Site admin' })
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('heading', { name: 'Site administration' })
      ).not.toBeInTheDocument();
    }
  );

  it('keeps public pages available when the visitor is not signed in', async () => {
    server.use(
      http.get('/api/user/me', () => new HttpResponse(null, { status: 401 }))
    );
    const rendered = renderRoute({ initialPath: '/about' });
    cleanup = rendered.cleanup;

    expect(
      await screen.findByRole('heading', { name: 'About GROVE' })
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(
        rendered.queryClient.getQueryState(meQueryOptions().queryKey)?.status
      ).toBe('error');
    });
    expect(rendered.router.state.location.pathname).toBe('/about');
    expect(
      screen.queryByRole('button', { name: 'Site admin' })
    ).not.toBeInTheDocument();
  });

  it('denies access when the API rejects a cached site admin user', async () => {
    silenceRouteErrors();
    mockUser();
    server.use(
      http.get(
        '/api/admin/access',
        () => new HttpResponse(null, { status: 403 })
      )
    );
    const queryClient = new QueryClient();
    queryClient.setQueryData(meQueryOptions().queryKey, siteAdmin);
    ({ cleanup } = renderRoute({ initialPath: '/admin', queryClient }));

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Site administration' })
    ).not.toBeInTheDocument();
  });

  it('shows a loading error when the admin access check fails on the server', async () => {
    silenceRouteErrors();
    mockUser();
    server.use(
      http.get(
        '/api/admin/access',
        () => new HttpResponse(null, { status: 500 })
      )
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin' }));

    expect(
      await screen.findByRole('heading', {
        name: 'We could not load site administration',
      })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Not authorized' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Site administration' })
    ).not.toBeInTheDocument();
  });

  it('replaces protected content when a background user check denies access despite a cached user', async () => {
    mockUser();
    server.use(
      http.get(
        '/api/admin/access',
        () => new HttpResponse(null, { status: 204 })
      )
    );
    const rendered = renderRoute({ initialPath: '/admin' });
    cleanup = rendered.cleanup;
    expect(
      await screen.findByRole('heading', { name: 'Site administration' })
    ).toBeInTheDocument();

    server.use(
      http.get('/api/user/me', () => new HttpResponse(null, { status: 403 }))
    );
    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: meQueryOptions().queryKey,
      });
    });

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.getByText("You don't have permission to view this page.")
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to Grove' })).toHaveAttribute(
      'href',
      '/temp'
    );
    expect(
      screen.queryByRole('heading', { name: 'Site administration' })
    ).not.toBeInTheDocument();
    expect(
      rendered.queryClient.getQueryData(meQueryOptions().queryKey)
    ).toMatchObject({ isSiteAdmin: true });
    expect(rendered.router.state.location.pathname).toBe('/admin');
  });

  it('hides the site admin menu after a background user check loses authentication', async () => {
    mockUser();
    const rendered = renderRoute({ initialPath: '/about' });
    cleanup = rendered.cleanup;
    expect(
      await screen.findByRole('button', { name: 'Site admin' })
    ).toBeInTheDocument();

    server.use(
      http.get('/api/user/me', () => new HttpResponse(null, { status: 401 }))
    );
    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: meQueryOptions().queryKey,
      });
    });

    await waitFor(() => {
      expect(
        screen.queryByRole('button', { name: 'Site admin' })
      ).not.toBeInTheDocument();
    });
    expect(
      rendered.queryClient.getQueryData(meQueryOptions().queryKey)
    ).toMatchObject({ isSiteAdmin: true });
    expect(
      screen.getByRole('heading', { name: 'About GROVE' })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe('/about');
  });

  it('redirects a protected route to login with its complete return URL', async () => {
    server.use(
      http.get('/api/user/me', () => new HttpResponse(null, { status: 401 }))
    );
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    cleanup = () => queryClient.clear();
    const href = '/me?view=profile#details';
    const context = {
      context: { queryClient },
      location: { href },
    } as Parameters<
      NonNullable<typeof AuthenticatedRoute.options.beforeLoad>
    >[0];

    await expect(
      AuthenticatedRoute.options.beforeLoad?.(context)
    ).rejects.toMatchObject({
      options: {
        href: `/login?returnUrl=${encodeURIComponent(href)}`,
        reloadDocument: true,
      },
    });
  });

  it('redirects an expired admin access check to login with its return URL', async () => {
    server.use(
      http.get(
        '/api/admin/access',
        () => new HttpResponse(null, { status: 401 })
      )
    );
    const href = '/admin?view=overview#settings';
    const context = { location: { href } } as Parameters<
      NonNullable<typeof AdminRoute.options.beforeLoad>
    >[0];

    await expect(
      AdminRoute.options.beforeLoad?.(context)
    ).rejects.toMatchObject({
      options: {
        href: `/login?returnUrl=${encodeURIComponent(href)}`,
        reloadDocument: true,
      },
    });
  });
});
