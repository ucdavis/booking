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
import type { TeamMember } from '@/features/teams/models/TeamMember.ts';
import { TeamRole } from '@/features/teams/models/TeamRole.ts';
import {
  teamMembersQueryKey,
  teamMembersQueryOptions,
} from '@/queries/teams.ts';
import type { User } from '@/queries/user.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

const user: User = {
  email: 'taylor@example.com',
  iamId: '100001',
  id: '1',
  isSiteAdmin: false,
  name: 'Taylor Admin',
  roles: [],
};

const team = { id: 1, name: 'Plant Sciences', slug: 'plant-sciences' };
const otherTeam = { id: 2, name: 'Animal Science', slug: 'animal-science' };
const currentMember: TeamMember = {
  email: user.email,
  iamId: '100001',
  id: 1,
  isActive: true,
  name: user.name,
  role: TeamRole.Admin,
};
const otherMember: TeamMember = {
  email: 'avery@example.com',
  iamId: '100002',
  id: 2,
  isActive: true,
  name: 'Avery Editor',
  role: TeamRole.Editor,
};
const person = {
  email: 'sam@example.com',
  iamId: '100003',
  isActive: true,
  isActiveInIam: true,
  kerberos: 'samsmith',
  name: 'Sam Smith',
  role: null,
};

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

function mockAccess({
  isSiteAdmin = false,
  role = 'admin',
}: { isSiteAdmin?: boolean; role?: string | null } = {}) {
  server.use(
    http.get('/api/user/me', () => HttpResponse.json({ ...user, isSiteAdmin })),
    http.get('/api/teams', () => HttpResponse.json([team])),
    http.get('/api/teams/:teamSlug', ({ params }) =>
      HttpResponse.json({
        isSiteAdmin,
        role,
        team: params.teamSlug === otherTeam.slug ? otherTeam : team,
      })
    ),
    http.get('/api/teams/plant-sciences/members', () =>
      HttpResponse.json([currentMember, otherMember])
    ),
    http.get('/api/antiforgery', () =>
      HttpResponse.json({
        formFieldName: '__RequestVerificationToken',
        requestToken: 'team-antiforgery-token',
      })
    )
  );
}

async function openAddDialog() {
  fireEvent.click(await screen.findByRole('button', { name: 'Add member' }));
  return within(screen.getByRole('dialog', { name: 'Add a team member' }));
}

function silenceRouteErrors() {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
}

describe('team members', () => {
  it.each([false, true])(
    'keeps the current user read-only, including site admins (%s)',
    async (isSiteAdmin) => {
      mockAccess({ isSiteAdmin });
      ({ cleanup } = renderRoute({
        initialPath: '/teams/plant-sciences/members',
      }));

      expect(
        await screen.findByRole('heading', { name: 'Team members' })
      ).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Members' })).toHaveAttribute(
        'href',
        '/teams/plant-sciences/members'
      );
      const ownRow = within(
        await screen.findByRole('row', { name: /Taylor Admin/ })
      );
      expect(ownRow.getByText('You')).toBeInTheDocument();
      expect(
        ownRow.queryByRole('button', { name: /Change role|Remove/ })
      ).not.toBeInTheDocument();
      expect(ownRow.queryByRole('combobox')).not.toBeInTheDocument();
      const otherRow = within(
        screen.getByRole('row', { name: /Avery Editor/ })
      );
      expect(
        otherRow.getByRole('button', { name: 'Change role for Avery Editor' })
      ).toBeEnabled();
      expect(
        otherRow.getByRole('button', { name: 'Remove Avery Editor from team' })
      ).toBeEnabled();
    }
  );

  it('searches the current team only on submission and excludes existing or inactive people', async () => {
    mockAccess();
    const searchRequests = vi.fn(({ request }: { request: Request }) => {
      expect(new URL(request.url).searchParams.get('query')).toBe(
        'sam@example.com'
      );
      return HttpResponse.json([
        person,
        { ...person, iamId: '100004', name: 'Existing Member', role: 'editor' },
        {
          ...person,
          iamId: '100005',
          isActive: false,
          name: 'Inactive Account',
        },
        {
          ...person,
          iamId: '100006',
          isActiveInIam: false,
          name: 'Inactive Directory Person',
        },
      ]);
    });
    server.use(
      http.get('/api/teams/plant-sciences/members/people', searchRequests)
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    const dialog = await openAddDialog();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: 'sam@example.com' } }
    );
    expect(searchRequests).not.toHaveBeenCalled();
    expect(dialog.getByRole('button', { name: 'Add member' })).toBeDisabled();
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));

    expect(
      await dialog.findByRole('radio', { name: 'Sam Smith' })
    ).toBeEnabled();
    expect(
      dialog.getByRole('radio', { name: 'Existing Member' })
    ).toBeDisabled();
    expect(
      dialog.getByRole('radio', { name: 'Inactive Account' })
    ).toBeDisabled();
    expect(
      dialog.getByRole('radio', { name: 'Inactive Directory Person' })
    ).toBeDisabled();
    expect(dialog.getByText('Inactive')).toBeInTheDocument();
    expect(dialog.getAllByText('Active')).toHaveLength(3);
    expect(dialog.getByText(/Already a team member/)).toBeInTheDocument();
    expect(searchRequests).toHaveBeenCalledTimes(1);
    fireEvent.click(dialog.getByRole('radio', { name: 'Sam Smith' }));
    expect(dialog.getByRole('button', { name: 'Add member' })).toBeEnabled();
  });

  it('allows a local user with unchecked IAM status to be selected for addition', async () => {
    mockAccess();
    server.use(
      http.get('/api/teams/plant-sciences/members/people', () =>
        HttpResponse.json([{ ...person, isActiveInIam: null }])
      )
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    const dialog = await openAddDialog();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: person.email } }
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));

    const match = await dialog.findByRole('radio', { name: 'Sam Smith' });
    expect(match).toBeEnabled();
    expect(match).toBeChecked();
    expect(dialog.getByText('Not checked')).toHaveClass('badge-neutral');
    expect(dialog.queryByText('Inactive')).not.toBeInTheDocument();
    expect(
      dialog.queryByText('This person is inactive in IAM and cannot be added.')
    ).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Add member' })).toBeEnabled();
  });

  it('defaults additions to Editor, offers only Admin and Editor, and posts the selected IAM ID with a team token', async () => {
    mockAccess();
    let added = false;
    let body: unknown;
    let token: string | null = null;
    server.use(
      http.get('/api/teams/plant-sciences/members/people', () =>
        HttpResponse.json([person])
      ),
      http.get('/api/teams/plant-sciences/members', () =>
        HttpResponse.json([
          currentMember,
          otherMember,
          ...(added ? [{ ...person, id: 3, role: 'editor' }] : []),
        ])
      ),
      http.post('/api/teams/plant-sciences/members', async ({ request }) => {
        body = await request.json();
        token = request.headers.get('RequestVerificationToken');
        added = true;
        return HttpResponse.json({ ...person, id: 3, role: 'editor' });
      })
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    const dialog = await openAddDialog();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: person.iamId } }
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));
    fireEvent.click(await dialog.findByRole('radio', { name: 'Sam Smith' }));
    const roleSelect = dialog.getByRole('combobox', { name: 'Team role' });
    expect(roleSelect).toHaveValue('editor');
    expect(
      within(roleSelect)
        .getAllByRole('option')
        .map((option) => option.textContent)
        .sort()
    ).toEqual(['Admin', 'Editor']);
    fireEvent.click(dialog.getByRole('button', { name: 'Add member' }));

    expect(
      await screen.findByRole('row', { name: /Sam Smith/ })
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    );
    expect(body).toEqual({ iamId: person.iamId, role: 'editor' });
    expect(token).toBe('team-antiforgery-token');
  });

  it('changes another member role only after explicit Save and allows cancelling the edit', async () => {
    mockAccess();
    let memberRole = 'editor';
    const changes: unknown[] = [];
    let token: string | null = null;
    server.use(
      http.get('/api/teams/plant-sciences/members', () =>
        HttpResponse.json([currentMember, { ...otherMember, role: memberRole }])
      ),
      http.put(
        '/api/teams/plant-sciences/members/2/role',
        async ({ request }) => {
          changes.push(await request.json());
          token = request.headers.get('RequestVerificationToken');
          memberRole = 'admin';
          return HttpResponse.json({ ...otherMember, role: memberRole });
        }
      )
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    fireEvent.click(
      await screen.findByRole('button', {
        name: 'Change role for Avery Editor',
      })
    );
    const newRole = screen.getByRole('combobox', { name: 'New role' });
    expect(newRole).toHaveValue('editor');
    expect(
      within(newRole)
        .getAllByRole('option')
        .map((option) => option.textContent)
        .sort()
    ).toEqual(['Admin', 'Editor']);
    fireEvent.change(newRole, { target: { value: 'admin' } });
    expect(changes).toEqual([]);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(
      screen.queryByRole('combobox', { name: 'New role' })
    ).not.toBeInTheDocument();
    expect(changes).toEqual([]);

    fireEvent.click(
      screen.getByRole('button', { name: 'Change role for Avery Editor' })
    );
    fireEvent.change(screen.getByRole('combobox', { name: 'New role' }), {
      target: { value: 'admin' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save role' }));

    await waitFor(() => {
      expect(
        within(screen.getByRole('row', { name: /Avery Editor/ })).getByText(
          'Admin'
        )
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('combobox', { name: 'New role' })
      ).not.toBeInTheDocument();
    });
    expect(changes).toEqual([{ role: 'admin' }]);
    expect(token).toBe('team-antiforgery-token');
  });

  it('requires removal confirmation and refreshes the team after removal', async () => {
    mockAccess();
    let removed = false;
    let token: string | null = null;
    server.use(
      http.get('/api/teams/plant-sciences/members', () =>
        HttpResponse.json(
          removed ? [currentMember] : [currentMember, otherMember]
        )
      ),
      http.delete('/api/teams/plant-sciences/members/2', ({ request }) => {
        removed = true;
        token = request.headers.get('RequestVerificationToken');
        return new HttpResponse(null, { status: 204 });
      })
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    fireEvent.click(
      await screen.findByRole('button', {
        name: 'Remove Avery Editor from team',
      })
    );
    expect(
      screen.getByRole('heading', { name: 'Remove team member?' })
    ).toBeInTheDocument();
    expect(removed).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(removed).toBe(false);
    expect(
      screen.queryByRole('button', { name: 'Confirm removal' })
    ).not.toBeInTheDocument();
    fireEvent.click(
      screen.getByRole('button', { name: 'Remove Avery Editor from team' })
    );
    fireEvent.click(screen.getByRole('button', { name: 'Confirm removal' }));

    await waitFor(() =>
      expect(
        screen.queryByRole('row', { name: /Avery Editor/ })
      ).not.toBeInTheDocument()
    );
    expect(
      screen.getByRole('row', { name: /Taylor Admin/ })
    ).toBeInTheDocument();
    expect(token).toBe('team-antiforgery-token');
  });

  it.each(['editor', 'viewer'])(
    'hides Members and denies direct member management for a team %s',
    async (role) => {
      silenceRouteErrors();
      mockAccess({ role });
      const membersRequests = vi.fn(
        () => new HttpResponse(null, { status: 403 })
      );
      server.use(
        http.get('/api/teams/plant-sciences/members', membersRequests)
      );
      const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
      cleanup = rendered.cleanup;
      await screen.findByRole('heading', { name: 'Team overview' });
      expect(
        screen.queryByRole('link', { name: 'Members' })
      ).not.toBeInTheDocument();

      await act(async () => {
        await rendered.router.navigate({
          params: { teamSlug: team.slug },
          to: '/teams/$teamSlug/members',
        });
      });

      expect(
        await screen.findByRole('heading', { name: 'Not authorized' })
      ).toBeInTheDocument();
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: 'Add member' })
      ).not.toBeInTheDocument();
    }
  );

  it('rechecks cached member access and denies a stale admin on direct navigation', async () => {
    silenceRouteErrors();
    mockAccess();
    server.use(
      http.get(
        '/api/teams/plant-sciences/members',
        () => new HttpResponse(null, { status: 403 })
      )
    );
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(
      teamMembersQueryOptions(team.slug, user.id).queryKey,
      [currentMember, otherMember]
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
      queryClient,
    }));

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('row', { name: /Avery Editor/ })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Add member' })
    ).not.toBeInTheDocument();
  });

  it('removes cached member details and controls after a denied background refresh', async () => {
    mockAccess();
    const rendered = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    });
    cleanup = rendered.cleanup;
    await screen.findByRole('row', { name: /Avery Editor/ });
    server.use(
      http.get(
        '/api/teams/plant-sciences/members',
        () => new HttpResponse(null, { status: 403 })
      )
    );

    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: teamMembersQueryKey(team.slug),
      });
    });

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Add member' })
    ).not.toBeInTheDocument();
  });

  it('keeps member data separate when a global admin switches teams', async () => {
    mockAccess({ isSiteAdmin: true, role: null });
    server.use(
      http.get('/api/teams', () => HttpResponse.json([team, otherTeam])),
      http.get('/api/teams/animal-science/members', () =>
        HttpResponse.json([
          { ...otherMember, id: 3, name: 'Animal Team Member' },
        ])
      )
    );
    const rendered = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    });
    cleanup = rendered.cleanup;
    await screen.findByRole('row', { name: /Avery Editor/ });
    fireEvent.click(await screen.findByRole('button', { name: team.name }));
    fireEvent.click(screen.getByRole('link', { name: otherTeam.name }));
    await screen.findByRole('heading', { level: 1, name: otherTeam.name });
    fireEvent.click(screen.getByRole('link', { name: 'Members' }));

    expect(
      await screen.findByRole('row', { name: /Animal Team Member/ })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('row', { name: /Avery Editor/ })
    ).not.toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe(
      '/teams/animal-science/members'
    );
    expect(
      rendered.queryClient.getQueryData(
        teamMembersQueryOptions(team.slug, user.id).queryKey
      )
    ).toEqual([currentMember, otherMember]);
  });
});
