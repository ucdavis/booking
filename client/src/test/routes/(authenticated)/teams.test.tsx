import { QueryClient } from '@tanstack/react-query';
import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TeamRole, teamRoleLabels } from '@/features/teams/models/TeamRole.ts';
import {
  myTeamsQueryOptions,
  teamAccessQueryOptions,
} from '@/queries/teams.ts';
import type { User } from '@/queries/user.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

const user: User = {
  email: 'taylor@example.com',
  iamId: '100001',
  id: '1',
  isSiteAdmin: false,
  name: 'Taylor',
  roles: [],
};

const firstTeam = { id: 1, name: 'Plant Sciences', slug: 'plant-sciences' };
const secondTeam = { id: 2, name: 'Animal Science', slug: 'animal-science' };
let cleanup: (() => void) | undefined;

afterEach(() => {
  cleanup?.();
  cleanup = undefined;
  vi.restoreAllMocks();
});

function mockUser(isSiteAdmin = false) {
  server.use(
    http.get('/api/user/me', () => HttpResponse.json({ ...user, isSiteAdmin }))
  );
}

function mockTeamAccess(role: 'admin' | 'editor' | 'viewer' | null = 'viewer') {
  server.use(
    http.get('/api/teams/:teamSlug', ({ params }) =>
      HttpResponse.json({
        isSiteAdmin: role === null,
        role,
        team: params.teamSlug === firstTeam.slug ? firstTeam : secondTeam,
      })
    )
  );
}

function silenceRouteErrors() {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
}

describe('profile team memberships', () => {
  it.each([1, 5, 6, 8])(
    'shows names and roles for at most five of %s memberships',
    async (count) => {
      mockUser();
      const roles = [TeamRole.Admin, TeamRole.Editor, TeamRole.Viewer];
      const teams = Array.from({ length: count }, (_, index) => ({
        id: index + 1,
        name: `Team ${index + 1}`,
        role: roles[index % roles.length],
        slug: `team-${index + 1}`,
      }));
      server.use(http.get('/api/teams', () => HttpResponse.json(teams)));
      const rendered = renderRoute({ initialPath: '/me' });
      cleanup = rendered.cleanup;

      const list = await screen.findByRole('list', {
        name: 'Team memberships',
      });
      const rows = within(list).getAllByRole('listitem');
      expect(rows).toHaveLength(Math.min(count, 5));
      for (const [index, row] of rows.entries()) {
        const team = teams[index];
        expect(
          within(row).getByRole('link', { name: team.name })
        ).toHaveAttribute('href', `/teams/${team.slug}`);
        expect(
          within(row).getByText(teamRoleLabels[team.role])
        ).toBeInTheDocument();
      }
      expect(screen.queryByText('Sign-in roles')).not.toBeInTheDocument();

      if (count > 5) {
        expect(
          screen.getByText(
            `And ${count - 5} more ${count === 6 ? 'team' : 'teams'}. Use the team menu to see all your teams.`
          )
        ).toBeInTheDocument();
        expect(within(list).queryByText('Team 6')).not.toBeInTheDocument();
        // The display cap must not truncate the shared navigation data.
        expect(
          rendered.queryClient.getQueryData(
            myTeamsQueryOptions(user.id).queryKey
          )
        ).toHaveLength(count);
        fireEvent.click(screen.getByRole('button', { name: 'Choose team' }));
        expect(
          screen.getByRole('link', { name: 'Team 6' })
        ).toBeInTheDocument();
      } else {
        expect(screen.queryByText(/more teams?\./)).not.toBeInTheDocument();
      }
    }
  );

  it.each([false, true])(
    'hides empty memberships while preserving site admin access (%s)',
    async (isSiteAdmin) => {
      mockUser(isSiteAdmin);
      const rendered = renderRoute({ initialPath: '/me' });
      cleanup = rendered.cleanup;

      await waitFor(() => {
        expect(
          rendered.queryClient.getQueryState(
            myTeamsQueryOptions(user.id).queryKey
          )?.status
        ).toBe('success');
        expect(screen.queryByText('Team memberships')).not.toBeInTheDocument();
      });
      expect(
        screen.queryByText('You do not have any team memberships.')
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('list', { name: 'Team memberships' })
      ).not.toBeInTheDocument();
      const adminAccessNote = screen.queryByText(
        'As a site administrator, you also have access to all teams.'
      );
      if (isSiteAdmin) {
        expect(adminAccessNote).toBeInTheDocument();
      } else {
        expect(adminAccessNote).not.toBeInTheDocument();
      }
    }
  );

  it('keeps profile details visible while teams load and allows retrying a failure', async () => {
    mockUser();
    const { promise, resolve } = Promise.withResolvers<void>();
    server.use(
      http.get('/api/teams', async () => {
        await promise;
        return new HttpResponse(null, { status: 403 });
      })
    );
    ({ cleanup } = renderRoute({ initialPath: '/me' }));

    expect(await screen.findByText('Loading your teams…')).toBeInTheDocument();
    expect(screen.getByText(user.email)).toBeInTheDocument();
    resolve();
    expect(
      await screen.findByText('We could not load your teams.')
    ).toBeInTheDocument();
    expect(
      screen.queryByText('You do not have any team memberships.')
    ).not.toBeInTheDocument();
    server.use(
      http.get('/api/teams', () =>
        HttpResponse.json([{ ...firstTeam, role: TeamRole.Editor }])
      )
    );
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    const list = await screen.findByRole('list', { name: 'Team memberships' });
    expect(
      within(list).getByRole('link', { name: firstTeam.name })
    ).toBeInTheDocument();
    expect(within(list).getByText('Editor')).toBeInTheDocument();
    expect(
      screen.queryByText('We could not load your teams.')
    ).not.toBeInTheDocument();
  });
});

describe('team administration navigation and access', () => {
  it('hides team navigation when the user has no memberships', async () => {
    mockUser();
    const rendered = renderRoute({ initialPath: '/about' });
    cleanup = rendered.cleanup;

    await waitFor(() => {
      expect(
        rendered.queryClient.getQueryState(
          myTeamsQueryOptions(user.id).queryKey
        )?.status
      ).toBe('success');
    });
    expect(
      screen.queryByRole('link', { name: 'Team Admin' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Choose team' })
    ).not.toBeInTheDocument();
  });

  it('links directly to the only team and renders its slug-based overview', async () => {
    mockUser();
    mockTeamAccess();
    server.use(http.get('/api/teams', () => HttpResponse.json([firstTeam])));
    const rendered = renderRoute({ initialPath: '/about' });
    cleanup = rendered.cleanup;

    const link = await screen.findByRole('link', { name: 'Team Admin' });
    expect(link).toHaveAttribute('href', '/teams/plant-sciences');
    fireEvent.click(link);

    expect(
      await screen.findByRole('heading', { level: 1, name: firstTeam.name })
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Overview' })).toHaveAttribute(
      'href',
      '/teams/plant-sciences'
    );
    expect(rendered.router.state.location.pathname).toBe(
      '/teams/plant-sciences'
    );
  });

  it('switches teams through the dropdown and follows browser history', async () => {
    mockUser();
    mockTeamAccess();
    server.use(
      http.get('/api/teams', () => HttpResponse.json([firstTeam, secondTeam]))
    );
    const rendered = renderRoute({ initialPath: '/about' });
    cleanup = rendered.cleanup;
    fireEvent.click(await screen.findByRole('button', { name: 'Choose team' }));
    fireEvent.click(screen.getByRole('link', { name: firstTeam.name }));

    const firstButton = await screen.findByRole('button', {
      name: firstTeam.name,
    });
    expect(firstButton).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(firstButton);
    fireEvent.click(screen.getByRole('link', { name: secondTeam.name }));
    expect(
      await screen.findByRole('heading', { level: 1, name: secondTeam.name })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: secondTeam.name })
    ).toHaveAttribute('aria-expanded', 'false');
    expect(rendered.router.state.location.pathname).toBe(
      '/teams/animal-science'
    );

    act(() => rendered.router.history.back());

    expect(
      await screen.findByRole('heading', { level: 1, name: firstTeam.name })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: firstTeam.name })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe(
      '/teams/plant-sciences'
    );
  });

  it.each(['admin', 'editor', 'viewer'] as const)(
    'allows the team landing page for the %s role',
    async (role) => {
      mockUser();
      mockTeamAccess(role);
      ({ cleanup } = renderRoute({ initialPath: '/teams/plant-sciences' }));

      expect(
        await screen.findByRole('heading', { level: 1, name: firstTeam.name })
      ).toBeInTheDocument();
      expect(
        screen.getByRole('heading', { name: 'Team overview' })
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: 'Create team' })
      ).not.toBeInTheDocument();
    }
  );

  it('allows a global admin without membership to open a team', async () => {
    mockUser(true);
    mockTeamAccess(null);
    ({ cleanup } = renderRoute({ initialPath: '/teams/plant-sciences' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: firstTeam.name })
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'All teams' })).toHaveAttribute(
      'href',
      '/admin/teams'
    );
    expect(
      screen.queryByRole('link', { name: 'Team Admin' })
    ).not.toBeInTheDocument();
  });

  it('denies a different team even when the user has another membership', async () => {
    silenceRouteErrors();
    mockUser();
    server.use(
      http.get('/api/teams', () => HttpResponse.json([firstTeam])),
      http.get(
        '/api/teams/animal-science',
        () => new HttpResponse(null, { status: 403 })
      )
    );
    ({ cleanup } = renderRoute({ initialPath: '/teams/animal-science' }));

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: secondTeam.name })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Team overview' })
    ).not.toBeInTheDocument();
  });

  it('rechecks cached team access before opening a direct link', async () => {
    silenceRouteErrors();
    mockUser();
    server.use(
      http.get(
        '/api/teams/plant-sciences',
        () => new HttpResponse(null, { status: 403 })
      )
    );
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(teamAccessQueryOptions(firstTeam.slug).queryKey, {
      isSiteAdmin: false,
      role: TeamRole.Admin,
      team: firstTeam,
    });
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences',
      queryClient,
    }));

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: firstTeam.name })
    ).not.toBeInTheDocument();
  });

  it('removes team content when access is revoked during a background refresh', async () => {
    mockUser();
    mockTeamAccess();
    const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
    cleanup = rendered.cleanup;
    await screen.findByRole('heading', { level: 1, name: firstTeam.name });
    server.use(
      http.get(
        '/api/teams/plant-sciences',
        () => new HttpResponse(null, { status: 403 })
      )
    );

    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: teamAccessQueryOptions(firstTeam.slug).queryKey,
      });
    });

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: firstTeam.name })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: 'Overview' })
    ).not.toBeInTheDocument();
  });

  it('shows a missing-team state for a nonexistent slug', async () => {
    silenceRouteErrors();
    mockUser(true);
    server.use(
      http.get(
        '/api/teams/missing',
        () => new HttpResponse(null, { status: 404 })
      )
    );
    ({ cleanup } = renderRoute({ initialPath: '/teams/missing' }));

    expect(
      await screen.findByRole('heading', { name: 'Team not found' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Team overview' })
    ).not.toBeInTheDocument();
  });

  it('hides cached membership navigation after its refresh is denied', async () => {
    mockUser();
    server.use(http.get('/api/teams', () => HttpResponse.json([firstTeam])));
    const rendered = renderRoute({ initialPath: '/about' });
    cleanup = rendered.cleanup;
    await screen.findByRole('link', { name: 'Team Admin' });
    server.use(
      http.get('/api/teams', () => new HttpResponse(null, { status: 403 }))
    );

    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: myTeamsQueryOptions(user.id).queryKey,
      });
    });

    const navigation = within(
      screen.getByRole('navigation', { name: 'Primary navigation' })
    );
    await waitFor(() => {
      expect(
        navigation.queryByRole('link', { name: 'Team Admin' })
      ).not.toBeInTheDocument();
    });
    expect(
      screen.getByRole('heading', { name: 'About Booking' })
    ).toBeInTheDocument();
  });
});
