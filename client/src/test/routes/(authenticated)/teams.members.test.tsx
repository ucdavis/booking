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
  invalidateTeamMemberQueries,
  myTeamsQueryOptions,
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

const team = {
  id: 1,
  name: 'Plant Sciences',
  roles: [TeamRole.Admin],
  slug: 'plant-sciences',
};
const otherTeam = {
  id: 2,
  name: 'Animal Science',
  roles: [TeamRole.Admin],
  slug: 'animal-science',
};
const currentMember: TeamMember = {
  email: user.email,
  iamId: '100001',
  id: 1,
  isActive: true,
  name: user.name,
  roles: [TeamRole.Admin],
};
const otherMember: TeamMember = {
  email: 'avery@example.com',
  iamId: '100002',
  id: 2,
  isActive: true,
  name: 'Avery Editor',
  roles: [TeamRole.Editor],
};
const person = {
  email: 'sam@example.com',
  iamId: '100003',
  isActive: true,
  kerberos: 'samsmith',
  name: 'Sam Smith',
  roles: [],
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
  roles = role ? [role] : [],
}: { isSiteAdmin?: boolean; role?: string | null; roles?: string[] } = {}) {
  server.use(
    http.get('/api/user/me', () => HttpResponse.json({ ...user, isSiteAdmin })),
    http.get('/api/teams', () => HttpResponse.json([team])),
    http.get('/api/teams/:teamSlug', ({ params }) =>
      HttpResponse.json({
        isSiteAdmin,
        roles,
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
  it.each(['active', 'none'] as const)(
    'discards an unfinished initial membership response after self removal with %s refetching',
    async (refetchType) => {
      let releaseInitialResponse!: () => void;
      let signalInitialRequest!: () => void;
      const heldResponse = new Promise<void>((resolve) => {
        releaseInitialResponse = resolve;
      });
      const initialRequestStarted = new Promise<void>((resolve) => {
        signalInitialRequest = resolve;
      });
      let requests = 0;
      server.use(
        http.get('/api/teams', async () => {
          requests += 1;
          if (requests === 1) {
            signalInitialRequest();
            await heldResponse;
            return HttpResponse.json([team]);
          }
          return HttpResponse.json([]);
        })
      );
      const queryClient = new QueryClient();
      const options = myTeamsQueryOptions(user.id);
      const initialRead = queryClient
        .fetchQuery(options)
        .catch(() => undefined);

      try {
        await initialRequestStarted;
        expect(queryClient.getQueryData(options.queryKey)).toBeUndefined();

        await invalidateTeamMemberQueries(queryClient, team.slug, refetchType);
        releaseInitialResponse();
        await initialRead;

        expect(queryClient.getQueryData(options.queryKey)).toBeUndefined();
        expect(await queryClient.fetchQuery(options)).toEqual([]);
        expect(queryClient.getQueryData(options.queryKey)).toEqual([]);
        expect(requests).toBe(2);
      } finally {
        releaseInitialResponse();
        await initialRead;
        queryClient.clear();
      }
    }
  );

  it('allows management when Admin is one of several assigned roles', async () => {
    mockAccess({ roles: ['viewer', 'admin'] });
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));

    expect(
      await screen.findByRole('button', {
        name: 'Manage roles for Avery Editor',
      })
    ).toBeEnabled();
    expect(screen.getByRole('link', { name: 'Members' })).toBeInTheDocument();
  });

  it.each([false, true])(
    'allows current-user role management, including site admins (%s)',
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
        ownRow.getByRole('button', { name: 'Manage roles for Taylor Admin' })
      ).toBeEnabled();
      expect(
        ownRow.getByRole('button', { name: 'Remove Taylor Admin from team' })
      ).toBeEnabled();
      const otherRow = within(
        screen.getByRole('row', { name: /Avery Editor/ })
      );
      expect(
        otherRow.getByRole('button', { name: 'Manage roles for Avery Editor' })
      ).toBeEnabled();
      expect(
        otherRow.getByRole('button', { name: 'Remove Avery Editor from team' })
      ).toBeEnabled();
    }
  );

  it.each([
    ['Manage roles for Avery Editor', 'Manage team roles'],
    ['Remove Avery Editor from team', 'Remove team member?'],
  ])(
    'closes the %s dialog on Escape and restores focus without submitting',
    async (buttonName, dialogName) => {
      mockAccess();
      const update = vi.fn(() => HttpResponse.json(otherMember));
      const remove = vi.fn(() => new HttpResponse(null, { status: 204 }));
      server.use(
        http.put('/api/teams/plant-sciences/members/2/roles', update),
        http.delete('/api/teams/plant-sciences/members/2', remove)
      );
      ({ cleanup } = renderRoute({
        initialPath: '/teams/plant-sciences/members',
      }));
      const opener = await screen.findByRole('button', { name: buttonName });
      opener.focus();
      fireEvent.click(opener);
      const dialog = screen.getByRole('dialog', { name: dialogName });
      expect(dialog).toHaveAttribute('open');

      fireEvent(dialog, new Event('cancel', { cancelable: true }));

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(opener).toHaveFocus();
      expect(update).not.toHaveBeenCalled();
      expect(remove).not.toHaveBeenCalled();
    }
  );

  it.each([false, true])(
    'saves own roles without another warning when Admin is retained (Editor initially %s)',
    async (hasEditor) => {
      mockAccess();
      let ownRoles = hasEditor
        ? [TeamRole.Admin, TeamRole.Editor]
        : [TeamRole.Admin];
      const changes: unknown[] = [];
      server.use(
        http.get('/api/teams/plant-sciences/members', () =>
          HttpResponse.json([
            { ...currentMember, roles: ownRoles },
            otherMember,
          ])
        ),
        http.put(
          '/api/teams/plant-sciences/members/1/roles',
          async ({ request }) => {
            changes.push(await request.json());
            ownRoles = hasEditor
              ? [TeamRole.Admin]
              : [TeamRole.Admin, TeamRole.Editor];
            return HttpResponse.json({ ...currentMember, roles: ownRoles });
          }
        )
      );
      const rendered = renderRoute({
        initialPath: '/teams/plant-sciences/members',
      });
      cleanup = rendered.cleanup;
      fireEvent.click(
        await screen.findByRole('button', {
          name: 'Manage roles for Taylor Admin',
        })
      );
      const dialog = within(
        screen.getByRole('dialog', { name: 'Manage team roles' })
      );
      fireEvent.click(dialog.getByRole('checkbox', { name: 'Editor' }));
      fireEvent.click(dialog.getByRole('button', { name: 'Save roles' }));

      await waitFor(() =>
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      );
      expect(changes).toEqual([
        { roles: hasEditor ? ['admin'] : ['admin', 'editor'] },
      ]);
      expect(rendered.router.state.location.pathname).toBe(
        '/teams/plant-sciences/members'
      );
      expect(
        screen.queryByRole('heading', { name: 'Remove your Admin role?' })
      ).not.toBeInTheDocument();
    }
  );

  it.each([false, true])(
    'confirms own Admin removal, preserves warning choices on Back and failure, and updates access (site admin %s)',
    async (isSiteAdmin) => {
      mockAccess({ isSiteAdmin });
      let ownRoles = [TeamRole.Admin, TeamRole.Editor];
      const changes: unknown[] = [];
      const deniedMemberRequests = vi.fn();
      server.use(
        http.get('/api/teams', () =>
          HttpResponse.json([{ ...team, roles: ownRoles }])
        ),
        http.get('/api/teams/plant-sciences', () =>
          HttpResponse.json({
            isSiteAdmin,
            roles: ownRoles,
            team: { ...team, roles: ownRoles },
          })
        ),
        http.get('/api/teams/plant-sciences/members', () => {
          if (!isSiteAdmin && !ownRoles.includes(TeamRole.Admin)) {
            deniedMemberRequests();
            return new HttpResponse(null, { status: 403 });
          }
          return HttpResponse.json([
            { ...currentMember, roles: ownRoles },
            otherMember,
          ]);
        }),
        http.put(
          '/api/teams/plant-sciences/members/1/roles',
          async ({ request }) => {
            changes.push(await request.json());
            if (changes.length === 1) {
              return new HttpResponse(null, { status: 500 });
            }
            ownRoles = [TeamRole.Editor];
            return HttpResponse.json({ ...currentMember, roles: ownRoles });
          }
        )
      );
      const rendered = renderRoute({
        initialPath: '/teams/plant-sciences/members',
      });
      cleanup = rendered.cleanup;
      fireEvent.click(
        await screen.findByRole('button', {
          name: 'Manage roles for Taylor Admin',
        })
      );
      const dialog = within(
        screen.getByRole('dialog', { name: 'Manage team roles' })
      );
      fireEvent.click(dialog.getByRole('checkbox', { name: 'Admin' }));
      fireEvent.click(dialog.getByRole('button', { name: 'Save roles' }));

      expect(
        await dialog.findByRole('heading', { name: 'Remove your Admin role?' })
      ).toBeInTheDocument();
      expect(
        dialog.getByText(
          isSiteAdmin
            ? /You will keep access to manage this team through site administration/
            : /You will no longer be able to manage team members or payment settings/
        )
      ).toBeInTheDocument();
      expect(changes).toEqual([]);
      fireEvent.click(dialog.getByRole('button', { name: 'Back to roles' }));
      expect(dialog.getByRole('checkbox', { name: 'Admin' })).not.toBeChecked();
      expect(dialog.getByRole('checkbox', { name: 'Editor' })).toBeChecked();
      fireEvent.click(dialog.getByRole('button', { name: 'Save roles' }));
      await dialog.findByRole('heading', { name: 'Remove your Admin role?' });
      expect(changes).toEqual([]);
      fireEvent.click(
        dialog.getByRole('button', { name: 'Confirm role change' })
      );

      expect(
        await dialog.findByText(/We could not change this member's roles/)
      ).toBeInTheDocument();
      expect(
        dialog.getByRole('heading', { name: 'Remove your Admin role?' })
      ).toBeInTheDocument();
      await waitFor(() =>
        expect(
          dialog.getByRole('button', { name: 'Confirm role change' })
        ).toBeEnabled()
      );
      fireEvent.click(
        dialog.getByRole('button', { name: 'Confirm role change' })
      );

      await waitFor(() =>
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      );
      expect(changes).toEqual([
        { confirmSelfAccessChange: true, roles: ['editor'] },
        { confirmSelfAccessChange: true, roles: ['editor'] },
      ]);
      await waitFor(() =>
        expect(rendered.router.state.location.pathname).toBe(
          isSiteAdmin
            ? '/teams/plant-sciences/members'
            : '/teams/plant-sciences'
        )
      );
      expect(
        await screen.findByRole('heading', {
          name: isSiteAdmin ? 'Team members' : 'Team overview',
        })
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('heading', { name: 'Not authorized' })
      ).not.toBeInTheDocument();
      expect(deniedMemberRequests).not.toHaveBeenCalled();
    }
  );

  it.each([false, true])(
    'warns before removing every own role, permits retry, and updates access (site admin %s)',
    async (isSiteAdmin) => {
      mockAccess({ isSiteAdmin });
      let removed = false;
      const removalQueries: string[] = [];
      const deniedMemberRequests = vi.fn();
      server.use(
        http.get('/api/teams', () => HttpResponse.json(removed ? [] : [team])),
        http.get('/api/teams/plant-sciences', () =>
          removed && !isSiteAdmin
            ? new HttpResponse(null, { status: 403 })
            : HttpResponse.json({
                isSiteAdmin,
                roles: removed ? [] : ['admin'],
                team,
              })
        ),
        http.get('/api/teams/plant-sciences/members', () => {
          if (removed && !isSiteAdmin) {
            deniedMemberRequests();
            return new HttpResponse(null, { status: 403 });
          }
          return HttpResponse.json(
            removed ? [otherMember] : [currentMember, otherMember]
          );
        }),
        http.delete('/api/teams/plant-sciences/members/1', ({ request }) => {
          removalQueries.push(new URL(request.url).search);
          if (removalQueries.length === 1) {
            return new HttpResponse(null, { status: 500 });
          }
          removed = true;
          return new HttpResponse(null, { status: 204 });
        })
      );
      const rendered = renderRoute({
        initialPath: '/teams/plant-sciences/members',
      });
      cleanup = rendered.cleanup;
      const opener = await screen.findByRole('button', {
        name: 'Remove Taylor Admin from team',
      });
      opener.focus();
      fireEvent.click(opener);
      let dialog = within(
        screen.getByRole('dialog', { name: 'Remove team member?' })
      );
      expect(
        dialog.getByText(/You are removing all of your team roles/)
      ).toBeInTheDocument();
      expect(
        dialog.getByText(
          isSiteAdmin
            ? /You will keep access through site administration/
            : /You will lose access to this team and be taken to your profile/
        )
      ).toBeInTheDocument();
      expect(removalQueries).toEqual([]);
      fireEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(opener).toHaveFocus();
      expect(removalQueries).toEqual([]);
      fireEvent.click(opener);
      dialog = within(
        screen.getByRole('dialog', { name: 'Remove team member?' })
      );
      fireEvent.click(dialog.getByRole('button', { name: 'Confirm removal' }));

      expect(
        await dialog.findByText(/We could not remove/)
      ).toBeInTheDocument();
      expect(
        dialog.getByText(/You are removing all of your team roles/)
      ).toBeInTheDocument();
      await waitFor(() =>
        expect(
          dialog.getByRole('button', { name: 'Confirm removal' })
        ).toBeEnabled()
      );
      fireEvent.click(dialog.getByRole('button', { name: 'Confirm removal' }));

      await waitFor(() =>
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      );
      expect(removalQueries).toEqual([
        '?confirmSelfAccessChange=true',
        '?confirmSelfAccessChange=true',
      ]);
      await waitFor(() =>
        expect(rendered.router.state.location.pathname).toBe(
          isSiteAdmin ? '/teams/plant-sciences/members' : '/me'
        )
      );
      expect(
        await screen.findByRole('heading', {
          name: isSiteAdmin ? 'Team members' : 'My profile',
        })
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('row', { name: /Taylor Admin/ })
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('heading', { name: 'Not authorized' })
      ).not.toBeInTheDocument();
      expect(deniedMemberRequests).not.toHaveBeenCalled();
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
        {
          ...person,
          iamId: '100004',
          name: 'Existing Member',
          roles: ['editor'],
        },
        {
          ...person,
          iamId: '100005',
          isActive: false,
          name: 'Inactive Account',
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
    expect(dialog.queryByText('IAM status')).not.toBeInTheDocument();
    expect(dialog.getByText(/Already a team member/)).toBeInTheDocument();
    expect(searchRequests).toHaveBeenCalledTimes(1);
    fireEvent.click(dialog.getByRole('radio', { name: 'Sam Smith' }));
    expect(dialog.getByRole('button', { name: 'Add member' })).toBeEnabled();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: 'another@example.com' } }
    );
    expect(dialog.getByRole('button', { name: 'Add member' })).toBeDisabled();
    expect(dialog.queryByRole('radio')).not.toBeInTheDocument();
  });

  it('automatically selects the only eligible user for addition', async () => {
    mockAccess();
    server.use(
      http.get('/api/teams/plant-sciences/members/people', () =>
        HttpResponse.json([person])
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
    expect(dialog.queryByText('IAM status')).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Add member' })).toBeEnabled();
  });

  it('defaults additions to Editor and submits multiple selected roles with a team token', async () => {
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
          ...(added ? [{ ...person, id: 3, roles: ['admin', 'editor'] }] : []),
        ])
      ),
      http.post('/api/teams/plant-sciences/members', async ({ request }) => {
        body = await request.json();
        token = request.headers.get('RequestVerificationToken');
        added = true;
        return HttpResponse.json({
          ...person,
          id: 3,
          roles: ['admin', 'editor'],
        });
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
    expect(dialog.getByRole('checkbox', { name: 'Editor' })).toBeChecked();
    expect(dialog.getByRole('checkbox', { name: 'Admin' })).not.toBeChecked();
    expect(
      dialog.queryByRole('checkbox', { name: 'Viewer' })
    ).not.toBeInTheDocument();
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Editor' }));
    expect(dialog.getByText('Select at least one role.')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Add member' })).toBeDisabled();
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Editor' }));
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Admin' }));
    fireEvent.click(dialog.getByRole('button', { name: 'Add member' }));

    expect(
      await screen.findByRole('row', { name: /Sam Smith/ })
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    );
    expect(body).toEqual({ iamId: person.iamId, roles: ['admin', 'editor'] });
    const memberRow = within(screen.getByRole('row', { name: /Sam Smith/ }));
    expect(memberRow.getByText('Admin')).toBeInTheDocument();
    expect(memberRow.getByText('Editor')).toBeInTheDocument();
    expect(screen.getAllByRole('row', { name: /Sam Smith/ })).toHaveLength(1);
    expect(token).toBe('team-antiforgery-token');
  });

  it('adds an Approver-only member and explains that approval permissions are not active', async () => {
    mockAccess();
    let added = false;
    let body: unknown;
    const approver = { ...person, id: 3, roles: [TeamRole.Approver] };
    server.use(
      http.get('/api/teams/plant-sciences/members/people', () =>
        HttpResponse.json([person])
      ),
      http.get('/api/teams/plant-sciences/members', () =>
        HttpResponse.json([
          currentMember,
          otherMember,
          ...(added ? [approver] : []),
        ])
      ),
      http.post('/api/teams/plant-sciences/members', async ({ request }) => {
        body = await request.json();
        added = true;
        return HttpResponse.json(approver);
      })
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
    expect(
      await dialog.findByRole('radio', { name: 'Sam Smith' })
    ).toBeChecked();
    const checkbox = dialog.getByRole('checkbox', { name: 'Approver' });
    expect(checkbox).not.toBeChecked();
    expect(checkbox).toHaveAccessibleDescription(
      'View this team. Approval permissions are not active yet.'
    );
    fireEvent.click(checkbox);
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Editor' }));
    fireEvent.click(dialog.getByRole('button', { name: 'Add member' }));

    const row = within(await screen.findByRole('row', { name: /Sam Smith/ }));
    expect(row.getByText('Approver')).toBeInTheDocument();
    expect(row.queryByText('Editor')).not.toBeInTheDocument();
    expect(row.queryByText('Admin')).not.toBeInTheDocument();
    expect(body).toEqual({ iamId: person.iamId, roles: ['approver'] });
  });

  it('adds and removes Approver while retaining another team role', async () => {
    mockAccess();
    let memberRoles = [TeamRole.Editor];
    const changes: { roles: TeamRole[] }[] = [];
    server.use(
      http.get('/api/teams/plant-sciences/members', () =>
        HttpResponse.json([
          currentMember,
          { ...otherMember, roles: memberRoles },
        ])
      ),
      http.put(
        '/api/teams/plant-sciences/members/2/roles',
        async ({ request }) => {
          const body = (await request.json()) as { roles: TeamRole[] };
          changes.push(body);
          memberRoles = body.roles;
          return HttpResponse.json({ ...otherMember, roles: memberRoles });
        }
      )
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    fireEvent.click(
      await screen.findByRole('button', {
        name: 'Manage roles for Avery Editor',
      })
    );
    let dialog = within(
      screen.getByRole('dialog', { name: 'Manage team roles' })
    );
    expect(dialog.getByRole('checkbox', { name: 'Editor' })).toBeChecked();
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Approver' }));
    fireEvent.click(dialog.getByRole('button', { name: 'Save roles' }));

    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    );
    const row = within(screen.getByRole('row', { name: /Avery Editor/ }));
    expect(row.getByText('Editor')).toBeInTheDocument();
    expect(row.getByText('Approver')).toBeInTheDocument();
    fireEvent.click(
      row.getByRole('button', { name: 'Manage roles for Avery Editor' })
    );
    dialog = within(screen.getByRole('dialog', { name: 'Manage team roles' }));
    expect(dialog.getByRole('checkbox', { name: 'Approver' })).toBeChecked();
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Approver' }));
    fireEvent.click(dialog.getByRole('button', { name: 'Save roles' }));

    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    );
    const updatedRow = within(
      screen.getByRole('row', { name: /Avery Editor/ })
    );
    expect(updatedRow.getByText('Editor')).toBeInTheDocument();
    expect(updatedRow.queryByText('Approver')).not.toBeInTheDocument();
    expect(changes).toEqual([
      { roles: ['approver', 'editor'] },
      { roles: ['editor'] },
    ]);
  });

  it('adds roles only after explicit Save and allows cancelling the edit', async () => {
    mockAccess();
    let memberRoles = ['editor'];
    const changes: unknown[] = [];
    let token: string | null = null;
    server.use(
      http.get('/api/teams/plant-sciences/members', () =>
        HttpResponse.json([
          currentMember,
          { ...otherMember, roles: memberRoles },
        ])
      ),
      http.put(
        '/api/teams/plant-sciences/members/2/roles',
        async ({ request }) => {
          changes.push(await request.json());
          token = request.headers.get('RequestVerificationToken');
          memberRoles = ['admin', 'editor'];
          return HttpResponse.json({ ...otherMember, roles: memberRoles });
        }
      )
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    fireEvent.click(
      await screen.findByRole('button', {
        name: 'Manage roles for Avery Editor',
      })
    );
    expect(
      screen.getByRole('dialog', { name: 'Manage team roles' })
    ).toHaveAttribute('open');
    expect(screen.getByRole('checkbox', { name: 'Editor' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Admin' })).not.toBeChecked();
    expect(
      screen.queryByRole('checkbox', { name: 'Viewer' })
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Admin' }));
    expect(changes).toEqual([]);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(
      screen.queryByRole('checkbox', { name: 'Admin' })
    ).not.toBeInTheDocument();
    expect(changes).toEqual([]);

    fireEvent.click(
      screen.getByRole('button', { name: 'Manage roles for Avery Editor' })
    );
    expect(screen.getByRole('checkbox', { name: 'Editor' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Admin' })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Save roles' })).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Admin' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save roles' }));

    await waitFor(() => {
      expect(
        within(screen.getByRole('row', { name: /Avery Editor/ })).getByText(
          'Admin'
        )
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('checkbox', { name: 'Admin' })
      ).not.toBeInTheDocument();
    });
    expect(changes).toEqual([{ roles: ['admin', 'editor'] }]);
    expect(
      within(screen.getByRole('row', { name: /Avery Editor/ })).getByText(
        'Editor'
      )
    ).toBeInTheDocument();
    expect(token).toBe('team-antiforgery-token');
  });

  it('retains the selected person and role after a failed addition and submits them on retry', async () => {
    mockAccess();
    const additions: unknown[] = [];
    server.use(
      http.get('/api/teams/plant-sciences/members/people', () =>
        HttpResponse.json([
          person,
          { ...person, iamId: '100004', name: 'Another Person' },
        ])
      ),
      http.post('/api/teams/plant-sciences/members', async ({ request }) => {
        additions.push(await request.json());
        return additions.length === 1
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json({ ...person, id: 3, roles: ['admin'] });
      })
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
    fireEvent.click(await dialog.findByRole('radio', { name: 'Sam Smith' }));
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Admin' }));
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Editor' }));
    fireEvent.click(dialog.getByRole('button', { name: 'Add member' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'We could not add this team member.'
    );
    expect(dialog.getByRole('radio', { name: 'Sam Smith' })).toBeChecked();
    expect(dialog.getByRole('checkbox', { name: 'Admin' })).toBeChecked();
    expect(dialog.getByRole('checkbox', { name: 'Editor' })).not.toBeChecked();
    await waitFor(() =>
      expect(dialog.getByRole('button', { name: 'Add member' })).toBeEnabled()
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Add member' }));

    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    );
    expect(additions).toEqual([
      { iamId: person.iamId, roles: ['admin'] },
      { iamId: person.iamId, roles: ['admin'] },
    ]);
  });

  it('retains a role edit after a failed save and allows retrying', async () => {
    mockAccess();
    const changes: unknown[] = [];
    server.use(
      http.put(
        '/api/teams/plant-sciences/members/2/roles',
        async ({ request }) => {
          changes.push(await request.json());
          return changes.length === 1
            ? new HttpResponse(null, { status: 500 })
            : HttpResponse.json({ ...otherMember, roles: ['admin'] });
        }
      )
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    fireEvent.click(
      await screen.findByRole('button', {
        name: 'Manage roles for Avery Editor',
      })
    );
    fireEvent.click(screen.getByRole('checkbox', { name: 'Admin' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Editor' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save roles' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      "We could not change this member's roles."
    );
    expect(screen.getByRole('checkbox', { name: 'Admin' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Editor' })).not.toBeChecked();
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Save roles' })).toBeEnabled()
    );
    fireEvent.click(screen.getByRole('button', { name: 'Save roles' }));

    await waitFor(() =>
      expect(
        screen.queryByRole('checkbox', { name: 'Admin' })
      ).not.toBeInTheDocument()
    );
    expect(changes).toEqual([{ roles: ['admin'] }, { roles: ['admin'] }]);
  });

  it('displays legacy Viewer alongside other roles and allows removing it while retaining membership', async () => {
    mockAccess();
    let memberRoles = ['editor', 'viewer'];
    const changes: unknown[] = [];
    server.use(
      http.get('/api/teams/plant-sciences/members', () =>
        HttpResponse.json([
          currentMember,
          { ...otherMember, roles: memberRoles },
        ])
      ),
      http.put(
        '/api/teams/plant-sciences/members/2/roles',
        async ({ request }) => {
          changes.push(await request.json());
          memberRoles = ['editor'];
          return HttpResponse.json({ ...otherMember, roles: memberRoles });
        }
      )
    );
    ({ cleanup } = renderRoute({
      initialPath: '/teams/plant-sciences/members',
    }));
    const row = within(
      await screen.findByRole('row', { name: /Avery Editor/ })
    );
    expect(row.getByText('Viewer')).toBeInTheDocument();
    expect(row.getByText('Editor')).toBeInTheDocument();
    expect(screen.getAllByRole('row', { name: /Avery Editor/ })).toHaveLength(
      1
    );
    fireEvent.click(
      row.getByRole('button', { name: 'Manage roles for Avery Editor' })
    );
    expect(screen.getByRole('checkbox', { name: 'Viewer' })).toBeChecked();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Viewer' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Editor' }));
    expect(screen.getByText('Select at least one role.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save roles' })).toBeDisabled();
    expect(changes).toEqual([]);
    fireEvent.click(screen.getByRole('checkbox', { name: 'Editor' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save roles' }));

    await waitFor(() => {
      expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
      expect(
        within(screen.getByRole('row', { name: /Avery Editor/ })).queryByText(
          'Viewer'
        )
      ).not.toBeInTheDocument();
    });
    expect(changes).toEqual([{ roles: ['editor'] }]);
    fireEvent.click(
      screen.getByRole('button', { name: 'Manage roles for Avery Editor' })
    );
    expect(
      screen.queryByRole('checkbox', { name: 'Viewer' })
    ).not.toBeInTheDocument();
  });

  it('requires removal confirmation and refreshes the team after removing all roles', async () => {
    mockAccess();
    let removed = false;
    let removalQuery: string | undefined;
    let token: string | null = null;
    server.use(
      http.get('/api/teams/plant-sciences/members', () =>
        HttpResponse.json(
          removed
            ? [currentMember]
            : [currentMember, { ...otherMember, roles: ['admin', 'editor'] }]
        )
      ),
      http.delete('/api/teams/plant-sciences/members/2', ({ request }) => {
        removed = true;
        removalQuery = new URL(request.url).search;
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
      screen.getByRole('dialog', { name: 'Remove team member?' })
    ).toHaveAttribute('open');
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
    expect(removalQuery).toBe('');
    expect(token).toBe('team-antiforgery-token');
  });

  it.each(['approver', 'editor', 'viewer'])(
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
