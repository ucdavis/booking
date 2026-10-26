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
import type { Space } from '@/features/spaces/models/Space.ts';
import { adminSpacesQueryKey } from '@/queries/admin-spaces.ts';
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
const existingSpace: Space = {
  address: '1 Science Way',
  description: 'Laboratories and teaching rooms.',
  id: 1,
  isActive: true,
  isOfficialFacility: true,
  name: 'Science Building',
  referenceNumber: 'SCI-001',
  slug: 'science-building',
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

function mockAdminAccess(spaces: Space[] = [existingSpace]) {
  server.use(
    http.get('/api/user/me', () => HttpResponse.json(siteAdmin)),
    http.get(
      '/api/admin/access',
      () => new HttpResponse(null, { status: 204 })
    ),
    http.get('/api/admin/spaces', () => HttpResponse.json(spaces)),
    http.get('/api/antiforgery', () =>
      HttpResponse.json({
        formFieldName: '__RequestVerificationToken',
        requestToken: 'test-antiforgery-token',
      })
    )
  );
}

async function openCreateDialog() {
  fireEvent.click(await screen.findByRole('button', { name: 'Create space' }));
  return within(screen.getByRole('dialog', { name: 'Create a space' }));
}

describe('site admin spaces', () => {
  it('opens the spaces list from both admin navigation entry points', async () => {
    mockAdminAccess();
    const rendered = renderRoute({ initialPath: '/admin' });
    cleanup = rendered.cleanup;
    const spacesLink = await screen.findByRole('link', {
      name: /Manage spaces/,
    });
    expect(spacesLink).toHaveAttribute('href', '/admin/spaces');
    fireEvent.click(await screen.findByRole('button', { name: 'Site admin' }));
    const navigation = within(
      screen.getByRole('navigation', { name: 'Primary navigation' })
    );
    fireEvent.click(navigation.getByRole('link', { name: 'Spaces' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Spaces' })
    ).toBeInTheDocument();
    expect(
      await screen.findByRole('row', { name: /Science Building/ })
    ).toHaveTextContent('science-building');
    expect(rendered.router.state.location.pathname).toBe('/admin/spaces');
    expect(screen.queryByText(/time zone/i)).not.toBeInTheDocument();
  });

  it('suggests an editable slug and creates a space without a time-zone payload', async () => {
    const spaces = [existingSpace];
    mockAdminAccess(spaces);
    let postedBody: unknown;
    let antiforgeryHeader: string | null = null;
    server.use(
      http.post('/api/admin/spaces', async ({ request }) => {
        postedBody = await request.json();
        antiforgeryHeader = request.headers.get('RequestVerificationToken');
        const created: Space = {
          address: '2 Garden Lane',
          description: 'An outdoor gathering space.',
          id: 2,
          isActive: false,
          isOfficialFacility: true,
          name: 'Campus Garden',
          referenceNumber: 'GARDEN-001',
          slug: 'garden',
        };
        spaces.push(created);
        return HttpResponse.json(created, { status: 201 });
      })
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/spaces' }));
    const dialog = await openCreateDialog();
    const name = dialog.getByRole('textbox', { name: 'Space name' });
    const slug = dialog.getByRole('textbox', { name: 'URL slug' });
    expect(dialog.queryByText(/time zone/i)).not.toBeInTheDocument();
    expect(
      dialog.getByRole('checkbox', { name: 'Official facility' })
    ).not.toBeChecked();
    expect(dialog.getByRole('checkbox', { name: 'Active' })).toBeChecked();
    expect(dialog.getByRole('button', { name: 'Create space' })).toBeDisabled();
    fireEvent.change(name, { target: { value: 'Campus Garden' } });
    expect(slug).toHaveValue('campus-garden');
    fireEvent.change(slug, { target: { value: 'garden' } });
    fireEvent.change(name, { target: { value: '  Campus Garden  ' } });
    expect(slug).toHaveValue('garden');
    fireEvent.change(dialog.getByRole('textbox', { name: 'Description' }), {
      target: { value: '  An outdoor gathering space.  ' },
    });
    fireEvent.change(dialog.getByRole('textbox', { name: 'Address' }), {
      target: { value: '  2 Garden Lane  ' },
    });
    fireEvent.click(
      dialog.getByRole('checkbox', { name: 'Official facility' })
    );
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Reference number' }),
      {
        target: { value: '  GARDEN-001  ' },
      }
    );
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Active' }));
    await waitFor(() =>
      expect(dialog.getByRole('button', { name: 'Create space' })).toBeEnabled()
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Create space' }));

    expect(
      await screen.findByRole('row', { name: /Campus Garden/ })
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    );
    expect(postedBody).toEqual({
      address: '2 Garden Lane',
      description: 'An outdoor gathering space.',
      isActive: false,
      isOfficialFacility: true,
      name: 'Campus Garden',
      referenceNumber: 'GARDEN-001',
      slug: 'garden',
    });
    expect(antiforgeryHeader).toBe('test-antiforgery-token');
    const freshDialog = await openCreateDialog();
    expect(
      freshDialog.getByRole('textbox', { name: 'Space name' })
    ).toHaveValue('');
    expect(freshDialog.getByRole('textbox', { name: 'URL slug' })).toHaveValue(
      ''
    );
    expect(
      freshDialog.getByRole('textbox', { name: 'Description' })
    ).toHaveValue('');
    expect(freshDialog.getByRole('textbox', { name: 'Address' })).toHaveValue(
      ''
    );
    expect(
      freshDialog.getByRole('textbox', { name: 'Reference number' })
    ).toHaveValue('');
    expect(
      freshDialog.getByRole('checkbox', { name: 'Official facility' })
    ).not.toBeChecked();
    expect(freshDialog.getByRole('checkbox', { name: 'Active' })).toBeChecked();
  });

  it('edits the metadata and flags, clears optional values, and locks the existing slug', async () => {
    const spaces = [existingSpace];
    mockAdminAccess(spaces);
    let savedBody: unknown;
    server.use(
      http.put('/api/admin/spaces/1', async ({ request }) => {
        savedBody = await request.json();
        spaces[0] = { ...existingSpace, ...(savedBody as Omit<Space, 'id'>) };
        return HttpResponse.json(spaces[0]);
      })
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/spaces' }));
    const row = within(
      await screen.findByRole('row', { name: /Science Building/ })
    );
    fireEvent.click(row.getByRole('button', { name: /Edit/ }));
    const dialog = within(screen.getByRole('dialog', { name: 'Edit space' }));
    expect(dialog.getByRole('textbox', { name: 'Space name' })).toHaveValue(
      existingSpace.name
    );
    expect(dialog.getByRole('textbox', { name: 'Description' })).toHaveValue(
      existingSpace.description
    );
    expect(dialog.getByRole('textbox', { name: 'Address' })).toHaveValue(
      existingSpace.address
    );
    expect(
      dialog.getByRole('textbox', { name: 'Reference number' })
    ).toHaveValue(existingSpace.referenceNumber);
    expect(
      dialog.getByRole('checkbox', { name: 'Official facility' })
    ).toBeChecked();
    expect(dialog.getByRole('checkbox', { name: 'Active' })).toBeChecked();
    expect(dialog.getByRole('textbox', { name: 'URL slug' })).toHaveAttribute(
      'readonly'
    );
    fireEvent.change(dialog.getByRole('textbox', { name: 'Space name' }), {
      target: { value: 'Science Hall' },
    });
    expect(dialog.getByRole('textbox', { name: 'URL slug' })).toHaveValue(
      existingSpace.slug
    );
    fireEvent.change(dialog.getByRole('textbox', { name: 'Description' }), {
      target: { value: 'Updated description.' },
    });
    fireEvent.change(dialog.getByRole('textbox', { name: 'Address' }), {
      target: { value: '  ' },
    });
    fireEvent.click(
      dialog.getByRole('checkbox', { name: 'Official facility' })
    );
    fireEvent.change(
      dialog.getByRole('textbox', { name: 'Reference number' }),
      {
        target: { value: '  ' },
      }
    );
    fireEvent.click(dialog.getByRole('checkbox', { name: 'Active' }));
    fireEvent.click(dialog.getByRole('button', { name: 'Save changes' }));

    expect(
      await screen.findByRole('row', { name: /Science Hall/ })
    ).toHaveTextContent(existingSpace.slug);
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    );
    expect(savedBody).toEqual({
      address: null,
      description: 'Updated description.',
      isActive: false,
      isOfficialFacility: false,
      name: 'Science Hall',
      referenceNumber: null,
      slug: 'science-building',
    });
  });

  it('keeps entered values after a duplicate slug and allows correction', async () => {
    const spaces = [existingSpace];
    mockAdminAccess(spaces);
    server.use(
      http.post('/api/admin/spaces', async ({ request }) => {
        const body = (await request.json()) as { name: string; slug: string };
        if (body.slug === existingSpace.slug) {
          return new HttpResponse(null, { status: 409 });
        }
        const created = { ...existingSpace, ...body, id: 2 };
        spaces.push(created);
        return HttpResponse.json(created, { status: 201 });
      })
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/spaces' }));
    const dialog = await openCreateDialog();
    fireEvent.change(dialog.getByRole('textbox', { name: 'Space name' }), {
      target: { value: existingSpace.name },
    });
    await waitFor(() =>
      expect(dialog.getByRole('button', { name: 'Create space' })).toBeEnabled()
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Create space' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      /slug is already in use/i
    );
    expect(dialog.getByRole('textbox', { name: 'Space name' })).toHaveValue(
      existingSpace.name
    );
    expect(dialog.getByRole('textbox', { name: 'URL slug' })).toHaveValue(
      existingSpace.slug
    );
    fireEvent.change(dialog.getByRole('textbox', { name: 'URL slug' }), {
      target: { value: 'science-annex' },
    });
    fireEvent.click(dialog.getByRole('button', { name: 'Create space' }));
    expect(
      await screen.findByRole('row', { name: /science-annex/ })
    ).toBeInTheDocument();
  });

  it('requires official reference numbers and keeps values after a reference conflict', async () => {
    const spaces = [existingSpace];
    mockAdminAccess(spaces);
    const post = vi.fn(async ({ request }: { request: Request }) => {
      const body = (await request.json()) as Omit<Space, 'id'>;
      if (body.referenceNumber === existingSpace.referenceNumber) {
        return HttpResponse.json(
          {
            code: 'duplicate_reference_number',
            detail: 'That reference number is already in use.',
            status: 409,
            title: 'Conflict',
          },
          {
            headers: { 'Content-Type': 'application/problem+json' },
            status: 409,
          }
        );
      }
      const created = { ...body, id: 2 };
      spaces.push(created);
      return HttpResponse.json(created, { status: 201 });
    });
    server.use(http.post('/api/admin/spaces', post));
    ({ cleanup } = renderRoute({ initialPath: '/admin/spaces' }));
    const dialog = await openCreateDialog();
    fireEvent.change(dialog.getByRole('textbox', { name: 'Space name' }), {
      target: { value: 'West Hall' },
    });
    fireEvent.click(
      dialog.getByRole('checkbox', { name: 'Official facility' })
    );
    const referenceNumber = dialog.getByRole('textbox', {
      name: 'Reference number',
    });
    fireEvent.blur(referenceNumber);
    expect(await dialog.findByRole('alert')).toHaveTextContent(
      /reference number/i
    );
    expect(dialog.getByRole('button', { name: 'Create space' })).toBeDisabled();
    expect(post).not.toHaveBeenCalled();

    fireEvent.change(referenceNumber, {
      target: { value: existingSpace.referenceNumber },
    });
    await waitFor(() =>
      expect(dialog.getByRole('button', { name: 'Create space' })).toBeEnabled()
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Create space' }));
    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'This reference number is already in use by another official facility.'
    );
    expect(dialog.getByRole('textbox', { name: 'Space name' })).toHaveValue(
      'West Hall'
    );
    expect(dialog.getByRole('textbox', { name: 'URL slug' })).toHaveValue(
      'west-hall'
    );
    expect(
      dialog.getByRole('checkbox', { name: 'Official facility' })
    ).toBeChecked();
    expect(referenceNumber).toHaveValue(existingSpace.referenceNumber);

    fireEvent.change(referenceNumber, { target: { value: 'WEST-001' } });
    fireEvent.click(dialog.getByRole('button', { name: 'Create space' }));
    expect(
      await screen.findByRole('row', { name: /West Hall/ })
    ).toBeInTheDocument();
    expect(post).toHaveBeenCalledTimes(2);
  });

  it('validates invalid slugs without posting and cancels without saving', async () => {
    mockAdminAccess();
    const post = vi.fn(() => HttpResponse.json(existingSpace));
    server.use(http.post('/api/admin/spaces', post));
    ({ cleanup } = renderRoute({ initialPath: '/admin/spaces' }));
    const dialog = await openCreateDialog();
    fireEvent.change(dialog.getByRole('textbox', { name: 'Space name' }), {
      target: { value: 'Campus Garden' },
    });
    const slug = dialog.getByRole('textbox', { name: 'URL slug' });
    fireEvent.change(slug, { target: { value: 'Campus Garden' } });
    fireEvent.blur(slug);
    expect(await dialog.findByRole('alert')).toHaveTextContent(
      /lowercase letters, numbers, and single hyphens/i
    );
    expect(dialog.getByRole('button', { name: 'Create space' })).toBeDisabled();
    fireEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
  });

  it('denies non-site-admins before loading spaces', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    mockAdminAccess();
    const list = vi.fn(() => HttpResponse.json([existingSpace]));
    server.use(
      http.get('/api/user/me', () =>
        HttpResponse.json({ ...siteAdmin, isSiteAdmin: false })
      ),
      http.get(
        '/api/admin/access',
        () => new HttpResponse(null, { status: 403 })
      ),
      http.get('/api/admin/spaces', list)
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/spaces' }));
    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(list).not.toHaveBeenCalled();
    expect(
      screen.queryByRole('button', { name: 'Create space' })
    ).not.toBeInTheDocument();
  });

  it('removes cached spaces when a refresh denies access', async () => {
    mockAdminAccess();
    const rendered = renderRoute({ initialPath: '/admin/spaces' });
    cleanup = rendered.cleanup;
    await screen.findByRole('row', { name: /Science Building/ });
    server.use(
      http.get(
        '/api/admin/spaces',
        () => new HttpResponse(null, { status: 403 })
      )
    );
    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: adminSpacesQueryKey,
      });
    });
    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Create space' })
    ).not.toBeInTheDocument();
  });
});
