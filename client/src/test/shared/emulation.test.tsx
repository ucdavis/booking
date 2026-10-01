import { fireEvent, screen, waitFor, within } from '@testing-library/react';
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
import type { EmulationCandidate } from '@/features/admin/models/EmulationCandidate.ts';
import { emulationIdentityStorageKey } from '@/queries/emulation.ts';
import type { User } from '@/queries/user.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

const siteAdmin: User = {
  email: 'admin@example.com',
  iamId: '100001',
  id: '1',
  isSiteAdmin: true,
  name: 'Taylor Admin',
  roles: [],
};

const candidate: EmulationCandidate = {
  email: 'sam@example.com',
  hasUserAccount: true,
  iamId: '100002',
  isActive: true,
  isActiveInIam: null,
  kerberos: null,
  name: 'Sam Smith',
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

function renderAccount(user = siteAdmin) {
  server.use(
    http.get('/api/user/me', () => HttpResponse.json(user)),
    http.get('/api/emulation/antiforgery', () =>
      HttpResponse.json({ token: 'test-emulation-antiforgery-token' })
    )
  );
  const rendered = renderRoute({ initialPath: '/about' });
  cleanup = rendered.cleanup;
  const navigate = vi
    .spyOn(rendered.router, 'navigate')
    .mockImplementation(async () => undefined);
  return { ...rendered, navigate };
}

async function openDialog() {
  fireEvent.click(await screen.findByRole('button', { name: 'Taylor Admin' }));
  fireEvent.click(screen.getByRole('button', { name: 'Emulate user' }));
  return within(screen.getByRole('dialog', { name: 'Emulate user' }));
}

async function search(query = 'sam@example.com') {
  const dialog = await openDialog();
  fireEvent.change(
    dialog.getByRole('textbox', { name: 'Email, IAM ID, or Kerberos ID' }),
    { target: { value: query } }
  );
  fireEvent.click(dialog.getByRole('button', { name: 'Search' }));
  return dialog;
}

describe('user emulation', () => {
  it('hides the start action for ordinary users', async () => {
    renderAccount({ ...siteAdmin, isSiteAdmin: false });
    fireEvent.click(
      await screen.findByRole('button', { name: 'Taylor Admin' })
    );

    expect(
      screen.queryByRole('button', { name: 'Emulate user' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Stop emulating' })
    ).not.toBeInTheDocument();
  });

  it('searches only on submission and returns focus to the account button when canceled', async () => {
    const lookup = vi.fn(() => HttpResponse.json([candidate]));
    server.use(http.get('/api/emulation/candidates', lookup));
    renderAccount();
    const dialog = await openDialog();
    const input = dialog.getByRole('textbox', {
      name: 'Email, IAM ID, or Kerberos ID',
    });
    expect(input).toHaveAttribute('maxLength', '128');
    fireEvent.change(input, { target: { value: 'sam@example.com' } });
    expect(lookup).not.toHaveBeenCalled();
    expect(
      dialog.getByRole('button', { name: 'Start emulating' })
    ).toBeDisabled();

    fireEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Taylor Admin' })).toHaveFocus();
  });

  it('starts an existing account without an IAM row and reloads the document after the POST completes', async () => {
    const publishIdentityChange = vi.spyOn(Storage.prototype, 'setItem');
    let searchedQuery: string | null = null;
    let postedBody: unknown;
    let verificationHeader: string | null = null;
    let starts = 0;
    const { promise: startReady, resolve: completeStart } =
      Promise.withResolvers<void>();
    server.use(
      http.get('/api/emulation/candidates', ({ request }) => {
        searchedQuery = new URL(request.url).searchParams.get('query');
        return HttpResponse.json([candidate]);
      }),
      http.post('/api/emulation/start', async ({ request }) => {
        starts += 1;
        postedBody = await request.json();
        verificationHeader = request.headers.get('RequestVerificationToken');
        await startReady;
        return new HttpResponse(null, { status: 204 });
      })
    );
    const { navigate } = renderAccount();
    const dialog = await search('  sam@example.com  ');
    expect(
      await dialog.findByRole('radio', { name: 'Sam Smith' })
    ).toBeChecked();
    expect(
      dialog.queryByText(/account will be created/)
    ).not.toBeInTheDocument();
    expect(searchedQuery).toBe('sam@example.com');

    fireEvent.click(dialog.getByRole('button', { name: 'Start emulating' }));
    const starting = await dialog.findByRole('button', { name: 'Starting…' });
    expect(starting).toBeDisabled();
    fireEvent.click(starting);
    expect(navigate).not.toHaveBeenCalled();
    expect(publishIdentityChange).not.toHaveBeenCalled();
    completeStart();

    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith({
        href: '/temp',
        reloadDocument: true,
      })
    );
    expect(starts).toBe(1);
    expect(navigate).toHaveBeenCalledOnce();
    expect(publishIdentityChange).toHaveBeenCalledExactlyOnceWith(
      emulationIdentityStorageKey,
      expect.any(String)
    );
    expect(postedBody).toEqual({ iamId: candidate.iamId });
    expect(verificationHeader).toBe('test-emulation-antiforgery-token');
  });

  it('requires selection among matches, blocks inactive people, and reviews new-account creation', async () => {
    server.use(
      http.get('/api/emulation/candidates', () =>
        HttpResponse.json([
          { ...candidate, hasUserAccount: false, isActiveInIam: true },
          {
            ...candidate,
            iamId: '100003',
            isActive: false,
            name: 'Inactive User',
          },
          {
            ...candidate,
            iamId: '100004',
            isActiveInIam: false,
            name: 'Inactive Person',
          },
        ])
      )
    );
    renderAccount();
    const dialog = await search();
    const activePerson = await dialog.findByRole('radio', {
      name: 'Sam Smith',
    });
    expect(activePerson).not.toBeChecked();
    expect(
      dialog.getByRole('button', { name: 'Start emulating' })
    ).toBeDisabled();
    expect(dialog.getByRole('radio', { name: 'Inactive User' })).toBeDisabled();
    expect(
      dialog.getByRole('radio', { name: 'Inactive Person' })
    ).toBeDisabled();

    fireEvent.click(activePerson);
    expect(
      dialog.getByText(/A Booking account will be created/)
    ).toBeInTheDocument();
    expect(
      dialog.getByRole('button', { name: 'Start emulating' })
    ).toBeEnabled();
    fireEvent.change(dialog.getByRole('textbox'), {
      target: { value: '100003' },
    });
    expect(dialog.queryByRole('radio')).not.toBeInTheDocument();
    expect(
      dialog.getByRole('button', { name: 'Start emulating' })
    ).toBeDisabled();
  });

  it('shows lookup permission errors without retrying and supports another search', async () => {
    const lookup = vi.fn(() =>
      HttpResponse.text('You no longer have permission to emulate users.', {
        status: 403,
      })
    );
    server.use(http.get('/api/emulation/candidates', lookup));
    renderAccount();
    const dialog = await search();

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'You no longer have permission to emulate users.'
    );
    expect(lookup).toHaveBeenCalledOnce();
    expect(
      dialog.getByRole('button', { name: 'Start emulating' })
    ).toBeDisabled();
    server.use(
      http.get('/api/emulation/candidates', () => HttpResponse.json([]))
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Search' }));
    expect(
      await dialog.findByText(
        'No people matched that email, IAM ID, or Kerberos ID.'
      )
    ).toBeInTheDocument();
    expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('retains the selected user after a rejected start and allows retry', async () => {
    const publishIdentityChange = vi.spyOn(Storage.prototype, 'setItem');
    server.use(
      http.get('/api/emulation/candidates', () =>
        HttpResponse.json([candidate])
      ),
      http.post('/api/emulation/start', () =>
        HttpResponse.text('This user is no longer eligible. Search again.', {
          status: 409,
        })
      )
    );
    const { navigate } = renderAccount();
    const dialog = await search();
    await dialog.findByRole('radio', { name: 'Sam Smith' });
    fireEvent.click(dialog.getByRole('button', { name: 'Start emulating' }));
    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'This user is no longer eligible. Search again.'
    );
    expect(navigate).not.toHaveBeenCalled();
    expect(publishIdentityChange).not.toHaveBeenCalled();

    server.use(
      http.post(
        '/api/emulation/start',
        () => new HttpResponse(null, { status: 204 })
      )
    );
    fireEvent.click(dialog.getByRole('button', { name: 'Start emulating' }));
    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith({
        href: '/temp',
        reloadDocument: true,
      })
    );
    expect(publishIdentityChange).toHaveBeenCalledOnce();
  });

  it('reloads after another tab changes identity and removes its listener on unmount', async () => {
    const rendered = renderAccount();
    await screen.findByRole('button', { name: 'Taylor Admin' });
    fireEvent(
      window,
      new StorageEvent('storage', {
        key: 'unrelated-setting',
        newValue: 'changed',
        storageArea: window.localStorage,
      })
    );
    expect(rendered.navigate).not.toHaveBeenCalled();

    const identityChanged = () =>
      new StorageEvent('storage', {
        key: emulationIdentityStorageKey,
        newValue: crypto.randomUUID(),
        storageArea: window.localStorage,
      });
    fireEvent(window, identityChanged());
    expect(rendered.navigate).toHaveBeenCalledExactlyOnceWith({
      href: '/temp',
      reloadDocument: true,
    });
    rendered.cleanup();
    cleanup = undefined;
    fireEvent(window, identityChanged());
    expect(rendered.navigate).toHaveBeenCalledOnce();
  });

  it.each([false, true])(
    'shows the emulation marker and stop action without offering nested emulation (target admin: %s)',
    async (isSiteAdmin) => {
      renderAccount({
        ...siteAdmin,
        isEmulating: true,
        isSiteAdmin,
        name: 'Sam Smith',
      });
      const menu = await screen.findByRole('button', {
        name: 'Sam Smith (Emulating)',
      });
      fireEvent.click(menu);

      expect(screen.getByText('(Emulating)')).toBeVisible();
      expect(
        screen.getByRole('button', { name: 'Stop emulating' })
      ).toBeEnabled();
      expect(
        screen.queryByRole('button', { name: 'Emulate user' })
      ).not.toBeInTheDocument();
    }
  );

  it('keeps the emulation marker on stop failure and reloads the document after retry succeeds', async () => {
    const publishIdentityChange = vi.spyOn(Storage.prototype, 'setItem');
    let verificationHeader: string | null = null;
    let postedBody: string | undefined;
    server.use(
      http.post(
        '/api/emulation/stop',
        () => new HttpResponse(null, { status: 503 })
      )
    );
    const { navigate } = renderAccount({
      ...siteAdmin,
      isEmulating: true,
      isSiteAdmin: false,
      name: 'Sam Smith',
    });
    fireEvent.click(
      await screen.findByRole('button', { name: 'Sam Smith (Emulating)' })
    );
    fireEvent.click(screen.getByRole('button', { name: 'Stop emulating' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Unable to stop emulating. Please try again.'
    );
    expect(screen.getByText('(Emulating)')).toBeVisible();
    expect(navigate).not.toHaveBeenCalled();
    expect(publishIdentityChange).not.toHaveBeenCalled();

    const { promise: stopReady, resolve: completeStop } =
      Promise.withResolvers<void>();
    server.use(
      http.post('/api/emulation/stop', async ({ request }) => {
        postedBody = await request.text();
        verificationHeader = request.headers.get('RequestVerificationToken');
        await stopReady;
        return new HttpResponse(null, { status: 204 });
      })
    );
    fireEvent.click(screen.getByRole('button', { name: 'Stop emulating' }));
    expect(
      await screen.findByRole('button', { name: 'Stopping emulation…' })
    ).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Log out' })).toBeDisabled();
    expect(navigate).not.toHaveBeenCalled();
    completeStop();

    await waitFor(() =>
      expect(navigate).toHaveBeenCalledWith({
        href: '/temp',
        reloadDocument: true,
      })
    );
    expect(verificationHeader).toBe('test-emulation-antiforgery-token');
    expect(postedBody).toBe('');
    expect(publishIdentityChange).toHaveBeenCalledExactlyOnceWith(
      emulationIdentityStorageKey,
      expect.any(String)
    );
  });
});
