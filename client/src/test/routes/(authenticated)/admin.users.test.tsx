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
import type { AdminPerson } from '@/features/admin/models/AdminPerson.ts';
import { type User } from '@/queries/user.ts';
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

const currentAdmin = {
  email: siteAdmin.email,
  iamId: siteAdmin.iamId,
  id: 1,
  isActive: true,
  name: siteAdmin.name,
};

const otherAdmin = {
  email: 'avery@example.com',
  iamId: '100002',
  id: 2,
  isActive: true,
  name: 'Avery Admin',
};

const person = {
  email: 'sam@example.com',
  iamId: '100003',
  isActive: true,
  isActiveInIam: true,
  isAdmin: false,
  kerberos: 'samsmith',
  name: 'Sam Smith',
} satisfies AdminPerson;

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
    http.get('/api/admin/users', () =>
      HttpResponse.json([currentAdmin, otherAdmin])
    ),
    http.get('/api/antiforgery', () =>
      HttpResponse.json({
        formFieldName: '__RequestVerificationToken',
        requestToken: 'test-antiforgery-token',
      })
    )
  );
}

async function openAddDialog() {
  fireEvent.click(
    await screen.findByRole('button', { name: 'Add admin user' })
  );
  return within(screen.getByRole('dialog', { name: 'Add a site admin user' }));
}

function silenceRouteErrors() {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
}

describe('site admin users', () => {
  it('lists admins while protecting the current admin and searching only on submission', async () => {
    mockAdminAccess();
    const peopleRequests = vi.fn(() => HttpResponse.json([person]));
    server.use(http.get('/api/admin/people', peopleRequests));
    ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Site admin users' })
    ).toBeInTheDocument();
    const table = within(await screen.findByRole('table'));
    const ownRow = table.getByRole('row', { name: /Taylor Admin/ });
    expect(within(ownRow).getByText('You')).toBeInTheDocument();
    expect(
      within(ownRow).queryByRole('button', { name: /Remove admin access/ })
    ).not.toBeInTheDocument();
    expect(
      table.getByRole('button', { name: 'Remove admin access for Avery Admin' })
    ).toBeInTheDocument();
    expect(peopleRequests).not.toHaveBeenCalled();

    const dialog = await openAddDialog();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: 'sam@example.com' } }
    );
    expect(peopleRequests).not.toHaveBeenCalled();
    expect(
      dialog.getByRole('button', { name: 'Add admin user' })
    ).toBeDisabled();
  });

  it('navigates directly to admin users from the site admin dropdown', async () => {
    mockAdminAccess();
    const rendered = renderRoute({ initialPath: '/admin' });
    cleanup = rendered.cleanup;
    fireEvent.click(await screen.findByRole('button', { name: 'Site admin' }));
    fireEvent.click(screen.getByRole('link', { name: 'Admin users' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Site admin users' })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe('/admin/users');
    expect(screen.getByRole('button', { name: 'Site admin' })).toHaveAttribute(
      'aria-expanded',
      'false'
    );
  });

  it.each(['sam@example.com', '100003', 'samsmith'])(
    'looks up the submitted identifier %s and displays the matched person',
    async (identifier) => {
      mockAdminAccess();
      const searchQueries: (string | null)[] = [];
      server.use(
        http.get('/api/admin/people', ({ request }) => {
          searchQueries.push(new URL(request.url).searchParams.get('query'));
          return HttpResponse.json([person]);
        })
      );
      ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));
      const dialog = await openAddDialog();

      fireEvent.change(
        dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
        { target: { value: identifier } }
      );
      fireEvent.click(dialog.getByRole('button', { name: 'Search' }));

      expect(
        await dialog.findByRole('radio', { name: /Sam Smith/ })
      ).toBeEnabled();
      expect(dialog.getByText(person.email)).toBeInTheDocument();
      expect(dialog.getByText(new RegExp(person.iamId))).toBeInTheDocument();
      expect(dialog.getByText(new RegExp(person.kerberos))).toBeInTheDocument();
      expect(dialog.getByText('IAM status')).toBeInTheDocument();
      expect(dialog.getByText('Active')).toBeInTheDocument();
      expect(searchQueries).toEqual([identifier]);
    }
  );

  it('adds the selected IAM ID with an antiforgery token and refreshes the admins', async () => {
    mockAdminAccess();
    let added = false;
    let postedBody: unknown;
    let antiforgeryHeader: string | null = null;
    let listRequests = 0;
    server.use(
      http.get('/api/admin/users', () => {
        listRequests += 1;
        return HttpResponse.json([
          currentAdmin,
          otherAdmin,
          ...(added ? [{ ...person, id: 3 }] : []),
        ]);
      }),
      http.get('/api/admin/people', () => HttpResponse.json([person])),
      http.post('/api/admin/users', async ({ request }) => {
        postedBody = await request.json();
        antiforgeryHeader = request.headers.get('RequestVerificationToken');
        added = true;
        return HttpResponse.json({ ...person, id: 3 });
      })
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));
    await screen.findByRole('row', { name: /Avery Admin/ });
    const dialog = await openAddDialog();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: person.email } }
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));
    fireEvent.click(await dialog.findByRole('radio', { name: /Sam Smith/ }));
    fireEvent.click(dialog.getByRole('button', { name: 'Add admin user' }));

    expect(
      await screen.findByRole('row', { name: /Sam Smith/ })
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(postedBody).toEqual({ iamId: person.iamId });
    expect(antiforgeryHeader).toBe('test-antiforgery-token');
    expect(listRequests).toBe(2);
  });

  it('clears the selected match when searching again and handles no matches', async () => {
    mockAdminAccess();
    server.use(
      http.get('/api/admin/people', ({ request }) =>
        HttpResponse.json(
          new URL(request.url).searchParams.get('query') === person.email
            ? [person]
            : []
        )
      )
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));
    const dialog = await openAddDialog();
    const input = dialog.getByRole('textbox', {
      name: 'Email, IAM ID, or Kerberos ID',
    });
    fireEvent.change(input, { target: { value: person.email } });
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));
    fireEvent.click(await dialog.findByRole('radio', { name: /Sam Smith/ }));
    expect(
      dialog.getByRole('button', { name: 'Add admin user' })
    ).toBeEnabled();

    fireEvent.click(dialog.getByRole('button', { name: 'Search again' }));
    expect(
      dialog.queryByRole('radio', { name: /Sam Smith/ })
    ).not.toBeInTheDocument();
    expect(
      dialog.getByRole('button', { name: 'Add admin user' })
    ).toBeDisabled();
    fireEvent.change(input, { target: { value: 'missing@example.com' } });
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));

    expect(
      await dialog.findByText(
        'No people matched that email, IAM ID, or Kerberos ID.'
      )
    ).toBeInTheDocument();
    expect(
      dialog.getByRole('button', { name: 'Add admin user' })
    ).toBeDisabled();
  });

  it('disables existing admins and inactive matches while allowing an active person', async () => {
    mockAdminAccess();
    server.use(
      http.get('/api/admin/people', () =>
        HttpResponse.json([
          { ...person, isAdmin: true },
          {
            ...person,
            iamId: '100004',
            isActive: false,
            name: 'Inactive Person',
          },
          {
            ...person,
            iamId: '100005',
            isActiveInIam: false,
            name: 'Inactive IAM Person',
          },
          { ...person, iamId: '100006', name: 'Active Person' },
        ])
      )
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));
    const dialog = await openAddDialog();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: person.email } }
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));

    expect(
      await dialog.findByRole('radio', { name: /Sam Smith/ })
    ).toBeDisabled();
    expect(
      dialog.getByRole('radio', { name: /Inactive Person/ })
    ).toBeDisabled();
    expect(
      dialog.getByRole('radio', { name: 'Inactive IAM Person' })
    ).toBeDisabled();
    expect(
      dialog.getByRole('button', { name: 'Add admin user' })
    ).toBeDisabled();

    fireEvent.click(dialog.getByRole('radio', { name: 'Active Person' }));
    expect(
      dialog.getByRole('button', { name: 'Add admin user' })
    ).toBeEnabled();
  });

  it('shows a sole IAM-inactive match without allowing an add request', async () => {
    mockAdminAccess();
    const addRequests = vi.fn(() => HttpResponse.json({ ...person, id: 3 }));
    server.use(
      http.get('/api/admin/people', () =>
        HttpResponse.json([{ ...person, isActiveInIam: false }])
      ),
      http.post('/api/admin/users', addRequests)
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));
    const dialog = await openAddDialog();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: person.email } }
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));

    const match = await dialog.findByRole('radio', { name: 'Sam Smith' });
    expect(match).toBeDisabled();
    expect(match).not.toBeChecked();
    expect(dialog.getByText('IAM status')).toBeInTheDocument();
    expect(dialog.getByText('Inactive')).toBeInTheDocument();
    expect(
      dialog.getByText('This person is inactive in IAM and cannot be added.')
    ).toBeInTheDocument();
    expect(
      dialog.queryByText(/Adding Sam Smith grants access/)
    ).not.toBeInTheDocument();
    const addButton = dialog.getByRole('button', { name: 'Add admin user' });
    expect(addButton).toBeDisabled();
    fireEvent.click(addButton);
    expect(addRequests).not.toHaveBeenCalled();
  });

  it('shows a search error and lets the user cancel without adding anyone', async () => {
    mockAdminAccess();
    const addRequests = vi.fn(() => new HttpResponse(null, { status: 204 }));
    server.use(
      http.get(
        '/api/admin/people',
        () => new HttpResponse(null, { status: 503 })
      ),
      http.post('/api/admin/users', addRequests)
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));
    const dialog = await openAddDialog();
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
      { target: { value: person.email } }
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(/search/i);
    expect(
      dialog.getByRole('button', { name: 'Add admin user' })
    ).toBeDisabled();
    fireEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(addRequests).not.toHaveBeenCalled();
  });

  it('requires removal confirmation and refreshes the list after removal', async () => {
    mockAdminAccess();
    let removed = false;
    let removedId: string | readonly string[] | undefined;
    let antiforgeryHeader: string | null = null;
    server.use(
      http.get('/api/admin/users', () =>
        HttpResponse.json(removed ? [currentAdmin] : [currentAdmin, otherAdmin])
      ),
      http.delete('/api/admin/users/:id', ({ params, request }) => {
        removedId = params.id;
        antiforgeryHeader = request.headers.get('RequestVerificationToken');
        removed = true;
        return new HttpResponse(null, { status: 204 });
      })
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));
    const removeButton = await screen.findByRole('button', {
      name: 'Remove admin access for Avery Admin',
    });
    fireEvent.click(removeButton);
    expect(screen.getByText('Remove admin access?')).toBeInTheDocument();
    expect(removed).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(
      screen.queryByRole('button', { name: 'Confirm removal' })
    ).not.toBeInTheDocument();
    expect(removed).toBe(false);

    fireEvent.click(
      screen.getByRole('button', {
        name: 'Remove admin access for Avery Admin',
      })
    );
    fireEvent.click(screen.getByRole('button', { name: 'Confirm removal' }));
    await waitFor(() => {
      expect(
        screen.queryByRole('row', { name: /Avery Admin/ })
      ).not.toBeInTheDocument();
    });
    expect(
      screen.getByRole('row', { name: /Taylor Admin/ })
    ).toBeInTheDocument();
    expect(removedId).toBe('2');
    expect(antiforgeryHeader).toBe('test-antiforgery-token');
  });

  it('denies a direct users-page request when the admin access check returns 403', async () => {
    silenceRouteErrors();
    mockAdminAccess();
    const listRequests = vi.fn(() => HttpResponse.json([currentAdmin]));
    server.use(
      http.get(
        '/api/admin/access',
        () => new HttpResponse(null, { status: 403 })
      ),
      http.get('/api/admin/users', listRequests)
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/users' }));

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'Site admin users' })
    ).not.toBeInTheDocument();
    expect(listRequests).not.toHaveBeenCalled();
  });

  it('removes the users table if its API denies a background refresh', async () => {
    mockAdminAccess();
    const rendered = renderRoute({ initialPath: '/admin/users' });
    cleanup = rendered.cleanup;
    await screen.findByRole('row', { name: /Avery Admin/ });
    server.use(
      http.get(
        '/api/admin/users',
        () => new HttpResponse(null, { status: 403 })
      )
    );

    await act(async () => {
      await rendered.queryClient.invalidateQueries();
    });

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});
