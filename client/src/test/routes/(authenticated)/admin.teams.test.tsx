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
import { adminTeamsQueryKey } from '@/queries/admin.ts';
import type { User } from '@/queries/user.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

const siteAdmin: User = {
  email: 'taylor@example.com',
  iamId: '100001',
  id: '1',
  isSiteAdmin: true,
  name: 'Taylor Admin',
  roles: [],
};

const existingTeam = { id: 1, name: 'Plant Sciences', slug: 'plant-sciences' };
let cleanup: (() => void) | undefined;
const originalShowModal = Object.getOwnPropertyDescriptor(
  HTMLDialogElement.prototype,
  'showModal'
);
const originalClose = Object.getOwnPropertyDescriptor(
  HTMLDialogElement.prototype,
  'close'
);

// jsdom does not implement these native dialog methods.
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
      this.dispatchEvent(new Event('close'));
    },
  });
});

afterAll(() => {
  if (originalShowModal) {
    Object.defineProperty(
      HTMLDialogElement.prototype,
      'showModal',
      originalShowModal
    );
  } else {
    Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
  }
  if (originalClose) {
    Object.defineProperty(HTMLDialogElement.prototype, 'close', originalClose);
  } else {
    Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  }
});

afterEach(() => {
  cleanup?.();
  cleanup = undefined;
  vi.restoreAllMocks();
});

function mockAdminAccess() {
  server.use(
    http.get('/api/user/me', () => HttpResponse.json(siteAdmin)),
    http.get(
      '/api/admin/access',
      () => new HttpResponse(null, { status: 204 })
    ),
    http.get('/api/admin/teams', () => HttpResponse.json([existingTeam])),
    http.get('/api/antiforgery', () =>
      HttpResponse.json({
        formFieldName: '__RequestVerificationToken',
        requestToken: 'test-antiforgery-token',
      })
    )
  );
}

async function openCreateDialog() {
  fireEvent.click(await screen.findByRole('button', { name: 'Create team' }));
  return within(screen.getByRole('dialog', { name: 'Create a team' }));
}

function silenceRouteErrors() {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
}

describe('site admin teams', () => {
  it('opens the all-teams list through site administration without a membership', async () => {
    mockAdminAccess();
    server.use(
      http.get('/api/teams/plant-sciences', () =>
        HttpResponse.json({ isSiteAdmin: true, role: null, team: existingTeam })
      )
    );
    const rendered = renderRoute({ initialPath: '/admin' });
    cleanup = rendered.cleanup;
    fireEvent.click(await screen.findByRole('button', { name: 'Site admin' }));
    const navigation = within(
      screen.getByRole('navigation', { name: 'Primary navigation' })
    );
    fireEvent.click(navigation.getByRole('link', { name: 'Teams' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Teams' })
    ).toBeInTheDocument();
    const row = within(
      await screen.findByRole('row', { name: /Plant Sciences/ })
    );
    const teamLink = row.getByRole('link', { name: existingTeam.name });
    expect(teamLink).toHaveAttribute('href', '/teams/plant-sciences');
    fireEvent.click(teamLink);

    expect(
      await screen.findByRole('heading', { level: 1, name: existingTeam.name })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe(
      '/teams/plant-sciences'
    );
  });

  it('suggests an editable slug, creates with antiforgery protection, and opens the new team', async () => {
    mockAdminAccess();
    const createdTeam = { id: 2, name: 'Animal Science', slug: 'animal-labs' };
    let postedBody: unknown;
    let antiforgeryHeader: string | null = null;
    server.use(
      http.post('/api/admin/teams', async ({ request }) => {
        postedBody = await request.json();
        antiforgeryHeader = request.headers.get('RequestVerificationToken');
        return HttpResponse.json(createdTeam, { status: 201 });
      }),
      http.get('/api/teams/animal-labs', () =>
        HttpResponse.json({ isSiteAdmin: true, role: null, team: createdTeam })
      )
    );
    const rendered = renderRoute({ initialPath: '/admin/teams' });
    cleanup = rendered.cleanup;
    const dialog = await openCreateDialog();
    expect(dialog.queryByLabelText('Payments API key')).not.toBeInTheDocument();
    expect(
      dialog.getByText(/name and URL slug cannot be changed after creation/)
    ).toBeInTheDocument();
    const name = dialog.getByRole('textbox', { name: 'Team name' });
    const slug = dialog.getByRole('textbox', { name: 'URL slug' });
    expect(dialog.getByRole('button', { name: 'Create team' })).toBeDisabled();
    fireEvent.change(name, { target: { value: 'Animal Science' } });
    expect(slug).toHaveValue('animal-science');
    fireEvent.change(slug, { target: { value: 'animal-labs' } });
    fireEvent.change(name, { target: { value: '  Animal Science  ' } });
    expect(slug).toHaveValue('animal-labs');
    expect(dialog.getByText('/teams/animal-labs')).toBeInTheDocument();
    await waitFor(() =>
      expect(dialog.getByRole('button', { name: 'Create team' })).toBeEnabled()
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Create team' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: createdTeam.name })
    ).toBeInTheDocument();
    expect(postedBody).toEqual({ name: 'Animal Science', slug: 'animal-labs' });
    expect(antiforgeryHeader).toBe('test-antiforgery-token');
    expect(rendered.router.state.location.pathname).toBe('/teams/animal-labs');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: 'Team Admin' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('textbox', { name: 'Team name' })
    ).not.toBeInTheDocument();
  });

  it('rejects an invalid slug before posting and cancels without creating a team', async () => {
    mockAdminAccess();
    const createRequests = vi.fn(() => HttpResponse.json(existingTeam));
    server.use(http.post('/api/admin/teams', createRequests));
    ({ cleanup } = renderRoute({ initialPath: '/admin/teams' }));
    const dialog = await openCreateDialog();
    fireEvent.change(dialog.getByRole('textbox', { name: 'Team name' }), {
      target: { value: 'Animal Science' },
    });
    fireEvent.change(dialog.getByRole('textbox', { name: 'URL slug' }), {
      target: { value: 'Animal Science' },
    });
    fireEvent.blur(dialog.getByRole('textbox', { name: 'URL slug' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      /lowercase letters, numbers, and single hyphens/
    );
    expect(dialog.getByRole('button', { name: 'Create team' })).toBeDisabled();
    fireEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(createRequests).not.toHaveBeenCalled();
  });

  it('retains entered values after a slug conflict and allows correction', async () => {
    mockAdminAccess();
    const postedSlugs: string[] = [];
    const createdTeam = { id: 2, name: 'Plant Sciences', slug: 'plant-labs' };
    server.use(
      http.post('/api/admin/teams', async ({ request }) => {
        const body = (await request.json()) as { slug: string };
        postedSlugs.push(body.slug);
        return body.slug === existingTeam.slug
          ? new HttpResponse(null, { status: 409 })
          : HttpResponse.json(createdTeam, { status: 201 });
      }),
      http.get('/api/teams/plant-labs', () =>
        HttpResponse.json({ isSiteAdmin: true, role: null, team: createdTeam })
      )
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/teams' }));
    const dialog = await openCreateDialog();
    fireEvent.change(dialog.getByRole('textbox', { name: 'Team name' }), {
      target: { value: existingTeam.name },
    });
    await waitFor(() =>
      expect(dialog.getByRole('button', { name: 'Create team' })).toBeEnabled()
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Create team' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'This URL slug is already in use. Choose another slug.'
    );
    expect(dialog.getByRole('textbox', { name: 'Team name' })).toHaveValue(
      existingTeam.name
    );
    expect(dialog.getByRole('textbox', { name: 'URL slug' })).toHaveValue(
      existingTeam.slug
    );
    fireEvent.change(dialog.getByRole('textbox', { name: 'URL slug' }), {
      target: { value: createdTeam.slug },
    });
    await waitFor(() =>
      expect(dialog.getByRole('button', { name: 'Create team' })).toBeEnabled()
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Create team' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: createdTeam.name })
    ).toBeInTheDocument();
    expect(postedSlugs).toEqual(['plant-sciences', 'plant-labs']);
  });

  it.each([
    [400, 'Check the team name and URL slug, then try again.'],
    [403, 'You no longer have permission to create teams.'],
    [503, 'We could not create the team. Please try again.'],
  ] as const)(
    'reports a failed creation (%s) without leaving the dialog',
    async (status, message) => {
      mockAdminAccess();
      server.use(
        http.post(
          '/api/admin/teams',
          () => new HttpResponse(null, { status })
        )
      );
      const rendered = renderRoute({ initialPath: '/admin/teams' });
      cleanup = rendered.cleanup;
      const dialog = await openCreateDialog();
      fireEvent.change(dialog.getByRole('textbox', { name: 'Team name' }), {
        target: { value: 'Animal Science' },
      });
      await waitFor(() =>
        expect(
          dialog.getByRole('button', { name: 'Create team' })
        ).toBeEnabled()
      );
      fireEvent.click(dialog.getByRole('button', { name: 'Create team' }));

      expect(await dialog.findByRole('alert')).toHaveTextContent(message);
      expect(dialog.getByRole('textbox', { name: 'Team name' })).toHaveValue(
        'Animal Science'
      );
      expect(rendered.router.state.location.pathname).toBe('/admin/teams');
    }
  );

  it('denies a non-site-admin before loading the all-teams list', async () => {
    silenceRouteErrors();
    mockAdminAccess();
    const listRequests = vi.fn(() => HttpResponse.json([existingTeam]));
    server.use(
      http.get('/api/user/me', () =>
        HttpResponse.json({
          ...siteAdmin,
          isSiteAdmin: false,
          roles: ['admin'],
        })
      ),
      http.get(
        '/api/admin/access',
        () => new HttpResponse(null, { status: 403 })
      ),
      http.get('/api/admin/teams', listRequests)
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/teams' }));

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Create team' })
    ).not.toBeInTheDocument();
    expect(listRequests).not.toHaveBeenCalled();
  });

  it('removes the list and creation action when a background refresh denies access', async () => {
    mockAdminAccess();
    const rendered = renderRoute({ initialPath: '/admin/teams' });
    cleanup = rendered.cleanup;
    await screen.findByRole('row', { name: /Plant Sciences/ });
    server.use(
      http.get(
        '/api/admin/teams',
        () => new HttpResponse(null, { status: 403 })
      )
    );

    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: adminTeamsQueryKey,
      });
    });

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Create team' })
    ).not.toBeInTheDocument();
  });
});
