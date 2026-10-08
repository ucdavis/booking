import { QueryClient } from '@tanstack/react-query';
import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import {
  afterAll,
  afterEach,
  beforeAll,
  describe,
  expect,
  it,
  vi,
} from 'vitest';
import type { TeamPaymentsSettings } from '@/features/teams/models/TeamPaymentsSettings.ts';
import { TeamRole, teamRoleLabels } from '@/features/teams/models/TeamRole.ts';
import {
  myTeamsQueryOptions,
  teamAccessQueryOptions,
  teamPaymentsQueryOptions,
} from '@/queries/teams.ts';
import { meQueryOptions, type User } from '@/queries/user.ts';
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
const originalShowModal = Object.getOwnPropertyDescriptor(
  HTMLDialogElement.prototype,
  'showModal'
);
const originalClose = Object.getOwnPropertyDescriptor(
  HTMLDialogElement.prototype,
  'close'
);

beforeAll(() => {
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
    configurable: true,
    value(this: HTMLDialogElement) {
      this.setAttribute('open', '');
    },
  });
  Object.defineProperty(HTMLDialogElement.prototype, 'close', {
    configurable: true,
    value(this: HTMLDialogElement) {
      this.removeAttribute('open');
    },
  });
});

afterAll(() => {
  for (const [method, descriptor] of [
    ['showModal', originalShowModal],
    ['close', originalClose],
  ] as const) {
    if (descriptor) {
      Object.defineProperty(HTMLDialogElement.prototype, method, descriptor);
    } else {
      Reflect.deleteProperty(HTMLDialogElement.prototype, method);
    }
  }
});

afterEach(() => {
  cleanup?.();
  cleanup = undefined;
  vi.restoreAllMocks();
});

const connectedPayments: TeamPaymentsSettings = {
  maskedApiKey: 'abc*********xyz',
  message: null,
  paymentsTeamName: 'Plant Sciences Payments',
  paymentsTeamSlug: 'plant-sciences-payments',
  status: 'valid',
};

function mockPayments(settings: TeamPaymentsSettings = connectedPayments) {
  server.use(
    http.get('/api/teams/:teamSlug/payments', () =>
      HttpResponse.json(settings)
    ),
    http.get('/api/antiforgery', () =>
      HttpResponse.json({ requestToken: 'payments-test-token' })
    )
  );
}

async function openPaymentsDialog(buttonName = 'Replace API key') {
  fireEvent.click(await screen.findByRole('button', { name: buttonName }));
  return within(screen.getByRole('dialog'));
}

describe('team payments settings', () => {
  it.each([false, true])(
    'allows a team admin or site admin without membership to save a key (%s)',
    async (isSiteAdmin) => {
      mockUser(isSiteAdmin);
      mockTeamAccess(isSiteAdmin ? null : 'admin');
      const newKey = 'new-payment-test-key-123';
      const settings: TeamPaymentsSettings = {
        ...connectedPayments,
        maskedApiKey: 'new*****************123',
        paymentsTeamName: 'New Payments Team',
        paymentsTeamSlug: 'new-payments-team',
      };
      let payments: TeamPaymentsSettings = {
        maskedApiKey: null,
        message: null,
        paymentsTeamName: null,
        paymentsTeamSlug: null,
        status: 'unconfigured',
      };
      const { promise, resolve } = Promise.withResolvers<void>();
      let postedKey: unknown;
      let token: string | null = null;
      mockPayments();
      server.use(
        http.get('/api/teams/:teamSlug/payments', () =>
          HttpResponse.json(payments)
        ),
        http.put('/api/teams/:teamSlug/payments', async ({ request }) => {
          postedKey = await request.json();
          token = request.headers.get('RequestVerificationToken');
          await promise;
          payments = settings;
          return HttpResponse.json(settings);
        })
      );
      const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
      cleanup = rendered.cleanup;
      await screen.findByText('Not configured');
      const dialog = await openPaymentsDialog('Set API key');
      const input = dialog.getByLabelText('Payments API key');
      expect(input).toHaveAttribute('type', 'password');
      expect(input).toHaveValue('');
      fireEvent.change(input, { target: { value: newKey } });
      fireEvent.click(dialog.getByRole('button', { name: 'Verify and save' }));
      await waitFor(() => expect(postedKey).toEqual({ apiKey: newKey }));
      expect(token).toBe('payments-test-token');
      expect(
        dialog.getByRole('button', { name: 'Verifying and saving…' })
      ).toBeDisabled();
      expect(
        rendered.queryClient
          .getMutationCache()
          .getAll()
          .map((mutation) => mutation.state.variables)
      ).toEqual([undefined]);
      expect(
        JSON.stringify(
          rendered.queryClient
            .getMutationCache()
            .getAll()
            .map((mutation) => mutation.state)
        )
      ).not.toContain(newKey);
      resolve();
      expect(
        await screen.findByText('Payments API key verified and saved.')
      ).toBeInTheDocument();
      expect(screen.getByText(settings.paymentsTeamName!)).toBeInTheDocument();
      expect(screen.getByText(settings.paymentsTeamSlug!)).toBeInTheDocument();
      expect(screen.getByText(settings.maskedApiKey!)).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(
        JSON.stringify(
          rendered.queryClient
            .getQueryCache()
            .getAll()
            .map((query) => query.state.data)
        )
      ).not.toContain(newKey);
      await waitFor(() =>
        expect(rendered.queryClient.getMutationCache().getAll()).toHaveLength(0)
      );
      const reopened = await openPaymentsDialog();
      expect(reopened.getByLabelText('Payments API key')).toHaveValue('');
    }
  );

  it.each(['editor', 'viewer'] as const)(
    'shows connection details without editing or the masked key for %s',
    async (role) => {
      mockUser();
      mockTeamAccess(role);
      mockPayments({ ...connectedPayments, maskedApiKey: null });
      ({ cleanup } = renderRoute({ initialPath: '/teams/plant-sciences' }));
      expect(await screen.findByText('Connected')).toBeInTheDocument();
      expect(
        screen.getByText(connectedPayments.paymentsTeamName!)
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: /(?:Set|Replace) API key/ })
      ).not.toBeInTheDocument();
      expect(screen.queryByText('Saved API key')).not.toBeInTheDocument();
    }
  );

  it.each([400, 503])(
    'keeps the saved settings and candidate after failure, without caching the candidate (%s)',
    async (status) => {
      mockUser();
      mockTeamAccess('admin');
      mockPayments();
      const candidate = 'rejected-test-key-987';
      server.use(
        http.put('/api/teams/:teamSlug/payments', () =>
          HttpResponse.json({ detail: candidate }, { status })
        )
      );
      const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
      cleanup = rendered.cleanup;
      await screen.findByText('Connected');
      const dialog = await openPaymentsDialog();
      fireEvent.change(dialog.getByLabelText('Payments API key'), {
        target: { value: candidate },
      });
      fireEvent.click(dialog.getByRole('button', { name: 'Verify and save' }));
      expect(await dialog.findByRole('alert')).toHaveTextContent(
        status === 400
          ? 'This API key could not be validated.'
          : 'We could not confirm the save.'
      );
      expect(dialog.getByLabelText('Payments API key')).toHaveValue(candidate);
      expect(
        screen.getByText(connectedPayments.maskedApiKey!)
      ).toBeInTheDocument();
      expect(
        screen.getByText(connectedPayments.paymentsTeamName!)
      ).toBeInTheDocument();
      expect(
        JSON.stringify(
          rendered.queryClient
            .getQueryCache()
            .getAll()
            .map((query) => query.state.data)
        )
      ).not.toContain(candidate);
      await waitFor(() =>
        expect(rendered.queryClient.getMutationCache().getAll()).toHaveLength(0)
      );
      fireEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
      const reopened = await openPaymentsDialog();
      expect(reopened.getByLabelText('Payments API key')).toHaveValue('');
    }
  );

  it('refreshes a saved key that was disabled later and distinguishes an outage', async () => {
    mockUser();
    mockTeamAccess('admin');
    mockPayments();
    ({ cleanup } = renderRoute({ initialPath: '/teams/plant-sciences' }));
    await screen.findByText('Connected');
    mockPayments({
      ...connectedPayments,
      paymentsTeamName: null,
      status: 'invalid',
    });
    fireEvent.click(screen.getByRole('button', { name: 'Check connection' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'API key is invalid or disabled'
    );
    expect(
      screen.getByText(connectedPayments.maskedApiKey!)
    ).toBeInTheDocument();
    expect(
      screen.getByText(connectedPayments.paymentsTeamSlug!)
    ).toBeInTheDocument();
    mockPayments({
      ...connectedPayments,
      paymentsTeamName: null,
      status: 'unavailable',
    });
    fireEvent.click(screen.getByRole('button', { name: 'Check connection' }));
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Unable to verify the connection'
      )
    );
    expect(
      screen.queryByText('API key is invalid or disabled')
    ).not.toBeInTheDocument();
  });

  it('hides the editor immediately when a save is denied and rechecks access', async () => {
    mockUser();
    mockTeamAccess('admin');
    mockPayments();
    const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
    cleanup = rendered.cleanup;
    await screen.findByText('Connected');
    const dialog = await openPaymentsDialog();
    server.use(
      http.put(
        '/api/teams/:teamSlug/payments',
        () => new HttpResponse(null, { status: 403 })
      ),
      http.get('/api/teams/plant-sciences', () =>
        HttpResponse.json({
          isSiteAdmin: false,
          role: 'viewer',
          team: firstTeam,
        })
      )
    );
    fireEvent.change(dialog.getByLabelText('Payments API key'), {
      target: { value: 'revoked-access-test-key' },
    });
    fireEvent.click(dialog.getByRole('button', { name: 'Verify and save' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: /(?:Set|Replace) API key/ })
      ).not.toBeInTheDocument();
      expect(
        rendered.queryClient.getQueryData(
          teamAccessQueryOptions(firstTeam.slug).queryKey
        )
      ).toMatchObject({ role: 'viewer' });
    });
    expect(screen.queryByText('Saved API key')).not.toBeInTheDocument();
  });

  it('clears the edit form when switching teams and never submits it to the next team', async () => {
    mockUser();
    mockTeamAccess('admin');
    mockPayments();
    const submissions = vi.fn(() => HttpResponse.json(connectedPayments));
    server.use(http.put('/api/teams/:teamSlug/payments', submissions));
    const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
    cleanup = rendered.cleanup;
    await screen.findByText('Connected');
    const dialog = await openPaymentsDialog();
    fireEvent.change(dialog.getByLabelText('Payments API key'), {
      target: { value: 'first-team-only-test-key' },
    });
    await act(async () => {
      await rendered.router.navigate({
        params: { teamSlug: secondTeam.slug },
        to: '/teams/$teamSlug',
      });
    });
    expect(
      await screen.findByRole('heading', { level: 1, name: secondTeam.name })
    ).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await screen.findByText('Connected');
    const newDialog = await openPaymentsDialog();
    expect(newDialog.getByLabelText('Payments API key')).toHaveValue('');
    expect(submissions).not.toHaveBeenCalled();
    expect(
      rendered.queryClient.getQueryData(
        teamPaymentsQueryOptions(secondTeam.slug, user.id).queryKey
      )
    ).toEqual(connectedPayments);
  });

  it('closes the editor and hides cached settings when a connection refresh is denied', async () => {
    mockUser();
    mockTeamAccess('admin');
    mockPayments();
    const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
    cleanup = rendered.cleanup;
    await screen.findByText('Connected');
    const dialog = await openPaymentsDialog();
    fireEvent.change(dialog.getByLabelText('Payments API key'), {
      target: { value: 'unsaved-private-test-key' },
    });
    server.use(
      http.get(
        '/api/teams/:teamSlug/payments',
        () => new HttpResponse(null, { status: 403 })
      )
    );
    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: teamPaymentsQueryOptions(firstTeam.slug, user.id).queryKey,
      });
    });
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Your access to these payments settings has changed.'
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(
      screen.queryByText(connectedPayments.maskedApiKey!)
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /(?:Set|Replace) API key/ })
    ).not.toBeInTheDocument();
  });

  it.each([false, true])(
    'clears the candidate and aborts any submission when the effective user changes (pending: %s)',
    async (isPending) => {
      mockUser();
      mockTeamAccess('admin');
      mockPayments();
      const requestStarted = Promise.withResolvers<void>();
      const response = Promise.withResolvers<void>();
      let wasAborted = false;
      const submissions = vi.fn(async ({ request }: { request: Request }) => {
        request.signal.addEventListener('abort', () => {
          wasAborted = true;
        });
        requestStarted.resolve();
        await response.promise;
        return HttpResponse.json(connectedPayments);
      });
      server.use(http.put('/api/teams/:teamSlug/payments', submissions));
      const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
      cleanup = rendered.cleanup;
      await screen.findByText('Connected');
      const dialog = await openPaymentsDialog();
      fireEvent.change(dialog.getByLabelText('Payments API key'), {
        target: { value: 'previous-identity-test-key' },
      });
      try {
        if (isPending) {
          fireEvent.click(
            dialog.getByRole('button', { name: 'Verify and save' })
          );
          await requestStarted.promise;
        }
        await act(async () => {
          rendered.queryClient.setQueryData(meQueryOptions().queryKey, {
            ...user,
            iamId: '100002',
            id: '2',
            name: 'Another User',
          });
        });
        await waitFor(() =>
          expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
        );
        if (isPending) {
          await waitFor(() => expect(wasAborted).toBe(true));
        }
        await screen.findByText('Connected');
        const reopened = await openPaymentsDialog();
        expect(reopened.getByLabelText('Payments API key')).toHaveValue('');
        expect(submissions).toHaveBeenCalledTimes(isPending ? 1 : 0);
        expect(
          screen.queryByText('Payments API key verified and saved.')
        ).not.toBeInTheDocument();
      } finally {
        response.resolve();
      }
    }
  );
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
