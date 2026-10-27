import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Link,
  Outlet,
  RouterProvider,
} from '@tanstack/react-router';
import { TanStackRouterDevtoolsInProd } from '@tanstack/react-router-devtools';
import { cleanup, render, screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

describe('router devtools', () => {
  it('renders the real panel and updates its pathname after navigation', async () => {
    const rootRoute = createRootRoute({
      component: () => (
        <>
          <Outlet />
          {/* The regular export renders nothing when NODE_ENV is test. */}
          <TanStackRouterDevtoolsInProd initialIsOpen />
        </>
      ),
    });
    const indexRoute = createRoute({
      component: () => <Link to="/about">Open about</Link>,
      getParentRoute: () => rootRoute,
      path: '/',
    });
    const aboutRoute = createRoute({
      component: () => <h1>Devtools test destination</h1>,
      getParentRoute: () => rootRoute,
      path: '/about',
    });
    const router = createRouter({
      history: createMemoryHistory({ initialEntries: ['/'] }),
      routeTree: rootRoute.addChildren([indexRoute, aboutRoute]),
    });

    try {
      await router.load();
      render(<RouterProvider router={router} />);

      const pathnameLabel = await screen.findByText('Pathname');
      expect(pathnameLabel).toBeVisible();
      const pathname = pathnameLabel.parentElement
        ?.nextElementSibling as HTMLElement;
      expect(pathname).toHaveTextContent(/^\/$/);

      await userEvent
        .setup()
        .click(screen.getByRole('link', { name: 'Open about' }));

      expect(
        await screen.findByRole('heading', {
          name: 'Devtools test destination',
        })
      ).toBeVisible();
      expect(await within(pathname).findByText('/about')).toBeVisible();
    } finally {
      cleanup();
    }
  });
});
