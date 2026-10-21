import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { createBrowserHistory } from '@tanstack/react-router';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ResourceDetail } from '@/features/resources/models/ResourceDetail.ts';
import type { ResourceOptions } from '@/features/resources/models/ResourceOptions.ts';
import type { SaveResourceRequest } from '@/features/resources/models/SaveResourceRequest.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

const team = {
  id: 1,
  name: 'Plant Sciences',
  roles: ['editor'],
  slug: 'plant-sciences',
};
const listPath = `/teams/${team.slug}/resources`;
const formJson = JSON.stringify({
  fields: [{ id: 'purpose', label: 'Purpose', type: 'input' }],
});
const detailsJson = JSON.stringify({
  billingRates: [
    { amount: '25.00', basis: 'hour', id: 'standard', name: 'Standard' },
  ],
  futureSetting: { retained: true },
  openingHours: { monday: [{ end: '17:00', start: '09:00' }] },
  schemaVersion: 1,
});

function resource(overrides: Partial<ResourceDetail> = {}): ResourceDetail {
  return {
    allowReservationSharing: false,
    approvalMode: 'manual',
    capacity: 4,
    category: 'Equipment',
    config: {
      detailsJson,
      detailsSchemaVersion: 1,
      formJson,
      formSchemaVersion: 2,
      id: 10,
      publishedAt: '2026-10-01T12:00:00Z',
      sourceTemplateId: 1,
      version: 1,
    },
    createdAt: '2026-10-01T12:00:00Z',
    description: 'Shared equipment',
    followTeamCalendar: true,
    id: 1,
    isActive: true,
    isReservable: true,
    name: 'Microscope',
    parentResourceId: null,
    publicAccess: 'availability',
    slug: 'microscope',
    spaceId: 2,
    spaceName: 'Teaching laboratory',
    teamId: team.id,
    updatedAt: '2026-10-01T12:00:00Z',
    ...overrides,
  };
}

const options: ResourceOptions = {
  spaces: [
    {
      address: null,
      id: 2,
      name: 'Teaching laboratory',
      timeZoneId: 'America/Los_Angeles',
    },
  ],
  templates: [
    {
      createdAt: '2026-10-01T12:00:00Z',
      description: 'Equipment booking',
      formJson,
      formSchemaVersion: 2,
      id: 1,
      isActive: true,
      name: 'Equipment template',
      resourceDefaultsJson: detailsJson,
      teamId: null,
      updatedAt: '2026-10-01T12:00:00Z',
    },
  ],
};

let cleanup: (() => void) | undefined;
afterEach(() => {
  cleanup?.();
  cleanup = undefined;
  vi.restoreAllMocks();
});

function mockAccess(role: string | null = 'editor', isSiteAdmin = false) {
  server.use(
    http.get('/api/user/me', () =>
      HttpResponse.json({
        email: 'editor@example.test',
        iamId: '10001',
        id: '1',
        isSiteAdmin,
        name: 'Editor',
        roles: [],
      })
    ),
    http.get('/api/teams', () => HttpResponse.json([team])),
    http.get('/api/teams/:teamSlug', ({ params }) =>
      HttpResponse.json({
        isSiteAdmin,
        roles: role ? [role] : [],
        team: { ...team, slug: params.teamSlug },
      })
    ),
    http.get('/api/antiforgery', () =>
      HttpResponse.json({
        formFieldName: '__RequestVerificationToken',
        requestToken: 'resource-token',
      })
    )
  );
}

function mockResources(
  initial: ResourceDetail[] = [resource()],
  availableOptions = options
) {
  const saved = new Map(initial.map((entry) => [entry.id, entry]));
  server.use(
    http.get('/api/teams/:teamSlug/resources', () =>
      HttpResponse.json([...saved.values()])
    ),
    http.get('/api/teams/:teamSlug/resources/options', () =>
      HttpResponse.json(availableOptions)
    ),
    http.get('/api/teams/:teamSlug/resources/:id', ({ params }) =>
      saved.has(Number(params.id))
        ? HttpResponse.json(saved.get(Number(params.id)))
        : new HttpResponse(null, { status: 404 })
    )
  );
  return saved;
}

describe('team resources', () => {
  it.each([
    ['editor', false],
    ['admin', false],
    [null, true],
  ] as const)(
    'lets %s / site admin %s find resource management',
    async (role, isSiteAdmin) => {
      mockAccess(role, isSiteAdmin);
      mockResources();
      ({ cleanup } = renderRoute({ initialPath: listPath }));
      expect(
        await screen.findByRole('row', { name: /Microscope/ })
      ).toBeInTheDocument();
      expect(
        within(
          screen.getByRole('navigation', { name: 'Team administration' })
        ).getByRole('link', { name: 'Resources' })
      ).toHaveAttribute('href', listPath);
      expect(
        screen.getByRole('link', { name: 'Create resource' })
      ).toHaveAttribute('href', `${listPath}/new`);
    }
  );

  it.each([
    ['viewer', ''],
    ['viewer', '/new'],
    ['viewer', '/1'],
    ['approver', ''],
    ['approver', '/new'],
    ['approver', '/1'],
  ])(
    'denies %s access to resources%s before loading private data',
    async (role, suffix) => {
      mockAccess(role);
      const privateRequest = vi.fn(
        () => new HttpResponse(null, { status: 403 })
      );
      server.use(
        http.get('/api/teams/:teamSlug/resources', privateRequest),
        http.get('/api/teams/:teamSlug/resources/:id', privateRequest)
      );
      ({ cleanup } = renderRoute({ initialPath: `${listPath}${suffix}` }));
      expect(
        await screen.findByRole('heading', { name: 'Not authorized' })
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('link', { name: 'Resources' })
      ).not.toBeInTheDocument();
      expect(privateRequest).not.toHaveBeenCalled();
    }
  );

  it('keeps inactive resources available through filters and search', async () => {
    mockAccess();
    mockResources([
      resource(),
      resource({ id: 2, isActive: false, name: 'Retired centrifuge' }),
    ]);
    ({ cleanup } = renderRoute({ initialPath: listPath }));
    await screen.findByRole('row', { name: /Microscope/ });
    expect(
      screen.queryByRole('row', { name: /Retired centrifuge/ })
    ).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Status'), {
      target: { value: 'inactive' },
    });
    expect(
      screen.getByRole('row', { name: /Retired centrifuge/ })
    ).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Search resources'), {
      target: { value: 'missing' },
    });
    expect(screen.getByRole('status')).toHaveTextContent('No resources match');
  });

  it('does not load an invalid resource identifier', async () => {
    mockAccess();
    const privateRequest = vi.fn(() => new HttpResponse(null, { status: 404 }));
    server.use(http.get('/api/teams/:teamSlug/resources/:id', privateRequest));
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/not-a-number` }));
    expect(
      await screen.findByRole('heading', { name: 'Resource not found' })
    ).toBeInTheDocument();
    expect(privateRequest).not.toHaveBeenCalled();
  });

  it('protects an unsaved draft and opens a fresh resource after discarding', async () => {
    mockAccess();
    mockResources([resource(), resource({ id: 2, name: 'Centrifuge' })]);
    const previousUrl = window.location.href;
    const previousState = window.history.state;
    window.history.replaceState(null, '', `${listPath}/1`);
    const history = createBrowserHistory();
    const rendered = renderRoute({ history });
    cleanup = () => {
      rendered.cleanup();
      history.destroy();
      window.history.replaceState(previousState, '', previousUrl);
    };
    const name = await screen.findByRole('textbox', { name: 'Resource name' });
    fireEvent.change(name, { target: { value: 'Unsaved microscope' } });
    act(() => {
      void rendered.router.navigate({ to: `${listPath}/2` });
    });
    fireEvent.click(
      await screen.findByRole('button', { name: 'Keep editing' })
    );
    expect(name).toHaveValue('Unsaved microscope');
    expect(rendered.router.state.location.pathname).toBe(`${listPath}/1`);
    act(() => {
      void rendered.router.navigate({ to: `${listPath}/2` });
    });
    fireEvent.click(
      await screen.findByRole('button', { name: 'Discard changes' })
    );
    await waitFor(() =>
      expect(
        screen.getByRole('textbox', { name: 'Resource name' })
      ).toHaveValue('Centrifuge')
    );
  });

  it('retains entered values after a stale save fails', async () => {
    mockAccess();
    mockResources();
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.put('/api/teams/:teamSlug/resources/:id', async ({ request }) => {
        requests.push((await request.json()) as SaveResourceRequest);
        return HttpResponse.json(
          'This resource has changed since you opened it.',
          { status: 409 }
        );
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
    const name = await screen.findByRole('textbox', { name: 'Resource name' });
    fireEvent.change(name, { target: { value: 'My unsaved edit' } });
    fireEvent.click(screen.getByRole('button', { name: /Save changes/ }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      expectedConfigId: 10,
      name: 'My unsaved edit',
      updatedAt: resource().updatedAt,
    });
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'changed since you opened'
    );
    expect(name).toHaveValue('My unsaved edit');
  });

  it('starts from a template and creates a new location with the resource', async () => {
    mockAccess();
    const saved = mockResources([], { ...options, spaces: [] });
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.post('/api/teams/:teamSlug/resources', async ({ request }) => {
        const body = (await request.json()) as SaveResourceRequest;
        requests.push(body);
        const created = resource({
          ...body,
          id: 2,
          name: body.name,
          spaceId: 3,
          spaceName: body.newSpace!.name,
        });
        saved.set(created.id, created);
        return HttpResponse.json(created, { status: 201 });
      })
    );
    const rendered = renderRoute({ initialPath: `${listPath}/new` });
    cleanup = rendered.cleanup;
    const template = await screen.findByRole('combobox', {
      name: 'Resource template',
    });
    expect(
      screen.queryByRole('textbox', { name: 'Resource name' })
    ).not.toBeInTheDocument();
    fireEvent.change(template, { target: { value: '1' } });
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Resource name' }),
      { target: { value: 'New microscope' } }
    );
    const category = screen.getByRole('combobox', { name: 'Category' });
    for (const name of ['Room', 'Bench', 'Equipment', 'Other']) {
      expect(
        within(category).getByRole('option', { name })
      ).toBeInTheDocument();
    }
    fireEvent.change(category, {
      target: {
        value: (
          within(category).getByRole('option', {
            name: 'Equipment',
          }) as HTMLOptionElement
        ).value,
      },
    });
    fireEvent.change(screen.getByRole('textbox', { name: 'Location name' }), {
      target: { value: 'Annex laboratory' },
    });
    fireEvent.change(
      screen.getByRole('textbox', { name: 'Address (optional)' }),
      { target: { value: '1 University Way' } }
    );
    expect(
      screen.queryByRole('textbox', { name: 'Time zone' })
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Hours and rates' }));
    expect(screen.getByLabelText('Monday opening time 1')).toHaveValue('09:00');
    expect(screen.getByRole('textbox', { name: 'Rate name 1' })).toHaveValue(
      'Standard'
    );
    fireEvent.click(screen.getByRole('button', { name: 'Create resource' }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      category: 'Equipment',
      name: 'New microscope',
      newSpace: {
        address: '1 University Way',
        name: 'Annex laboratory',
      },
      sourceTemplateId: 1,
      spaceId: null,
    });
    // Unedited configuration is copied from the template by the server.
    expect(requests[0].newSpace).not.toHaveProperty('timeZoneId');
    expect(requests[0]).not.toHaveProperty('formJson');
    expect(requests[0]).not.toHaveProperty('detailsJson');
    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(`${listPath}/2`)
    );
    expect(
      await screen.findByRole('textbox', { name: 'Resource name' })
    ).toHaveValue('New microscope');
  });

  it('requires valid resource and location fields before creating a resource', async () => {
    mockAccess();
    mockResources([], { ...options, spaces: [] });
    const create = vi.fn(() => HttpResponse.json(resource(), { status: 201 }));
    server.use(http.post('/api/teams/:teamSlug/resources', create));
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/new` }));
    fireEvent.change(
      await screen.findByRole('combobox', { name: 'Resource template' }),
      { target: { value: '1' } }
    );
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Capacity (optional)' }),
      { target: { value: '-1' } }
    );
    fireEvent.click(screen.getByRole('button', { name: 'Create resource' }));
    await waitFor(() =>
      expect(
        screen.getByRole('textbox', { name: 'Resource name' })
      ).toHaveAttribute('aria-invalid', 'true')
    );
    expect(screen.getByRole('combobox', { name: 'Category' })).toHaveAttribute(
      'aria-invalid',
      'true'
    );
    expect(
      screen.getByRole('textbox', { name: 'Capacity (optional)' })
    ).toHaveAttribute('aria-invalid', 'true');
    expect(
      screen.getByRole('textbox', { name: 'Location name' })
    ).toHaveAttribute('aria-invalid', 'true');
    expect(create).not.toHaveBeenCalled();
  });

  it('requires a custom category for Other and saves its trimmed value', async () => {
    mockAccess();
    const saved = mockResources();
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.put('/api/teams/:teamSlug/resources/:id', async ({ request }) => {
        const body = (await request.json()) as SaveResourceRequest;
        requests.push(body);
        const previous = saved.get(1)!;
        const updated = {
          ...previous,
          ...body,
          spaceId: previous.spaceId,
          updatedAt: '2026-10-02T12:00:00Z',
        };
        saved.set(1, updated);
        return HttpResponse.json(updated);
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
    const category = await screen.findByRole('combobox', { name: 'Category' });
    fireEvent.change(category, {
      target: {
        value: (
          within(category).getByRole('option', {
            name: 'Other',
          }) as HTMLOptionElement
        ).value,
      },
    });
    const custom = await screen.findByRole('textbox', {
      name: 'Other category',
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(custom).toHaveAttribute('aria-invalid', 'true'));
    expect(requests).toHaveLength(0);
    fireEvent.change(custom, { target: { value: 'A'.repeat(51) } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(custom).toHaveAttribute('aria-invalid', 'true'));
    expect(requests).toHaveLength(0);
    fireEvent.change(custom, { target: { value: '  Greenhouse  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0].category).toBe('Greenhouse');
    expect(await screen.findByText('Resource saved.')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Other category' })).toHaveValue(
      'Greenhouse'
    );
  });

  it('keeps an existing custom category when only resource metadata changes', async () => {
    mockAccess();
    const original = resource({ category: 'Cold storage' });
    const saved = mockResources([original]);
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.put('/api/teams/:teamSlug/resources/:id', async ({ request }) => {
        const body = (await request.json()) as SaveResourceRequest;
        requests.push(body);
        const updated = {
          ...original,
          ...body,
          spaceId: original.spaceId,
          updatedAt: '2026-10-02T12:00:00Z',
        };
        saved.set(1, updated);
        return HttpResponse.json(updated);
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
    expect(
      await screen.findByRole('combobox', { name: 'Category' })
    ).toHaveDisplayValue('Other');
    expect(screen.getByRole('textbox', { name: 'Other category' })).toHaveValue(
      'Cold storage'
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Resource name' }), {
      target: { value: 'Renamed microscope' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      category: 'Cold storage',
      name: 'Renamed microscope',
    });
    expect(await screen.findByText('Resource saved.')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Other category' })).toHaveValue(
      'Cold storage'
    );
  });

  it('saves a preset category after switching away from invalid custom text', async () => {
    mockAccess();
    const saved = mockResources([resource({ category: 'room' })]);
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.put('/api/teams/:teamSlug/resources/:id', async ({ request }) => {
        const body = (await request.json()) as SaveResourceRequest;
        requests.push(body);
        const previous = saved.get(1)!;
        const updated = {
          ...previous,
          ...body,
          spaceId: previous.spaceId,
          updatedAt: '2026-10-02T12:00:00Z',
        };
        saved.set(1, updated);
        return HttpResponse.json(updated);
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
    const category = await screen.findByRole('combobox', { name: 'Category' });
    expect(category).toHaveDisplayValue('Room');
    expect(
      screen.queryByRole('textbox', { name: 'Other category' })
    ).not.toBeInTheDocument();
    fireEvent.change(category, {
      target: {
        value: (
          within(category).getByRole('option', {
            name: 'Other',
          }) as HTMLOptionElement
        ).value,
      },
    });
    const custom = await screen.findByRole('textbox', {
      name: 'Other category',
    });
    fireEvent.change(custom, { target: { value: 'A'.repeat(51) } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(custom).toHaveAttribute('aria-invalid', 'true'));
    expect(requests).toHaveLength(0);
    fireEvent.change(category, {
      target: {
        value: (
          within(category).getByRole('option', {
            name: 'Bench',
          }) as HTMLOptionElement
        ).value,
      },
    });
    expect(
      screen.queryByRole('textbox', { name: 'Other category' })
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0].category).toBe('Bench');
    expect(await screen.findByText('Resource saved.')).toBeInTheDocument();
  });

  it('preserves unknown settings while editing a copied billing rate', async () => {
    mockAccess();
    const saved = mockResources();
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.put('/api/teams/:teamSlug/resources/:id', async ({ request }) => {
        const body = (await request.json()) as SaveResourceRequest;
        requests.push(body);
        const previous = saved.get(1)!;
        const updated = resource({
          ...body,
          config: {
            ...previous.config!,
            detailsJson: body.detailsJson!,
            id: 11,
            version: 2,
          },
          spaceId: previous.spaceId,
          updatedAt: '2026-10-02T12:00:00Z',
        });
        saved.set(1, updated);
        return HttpResponse.json(updated);
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
    fireEvent.click(
      await screen.findByRole('button', { name: 'Hours and rates' })
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Rate name 1' }), {
      target: { value: 'Updated rate' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(JSON.parse(requests[0].detailsJson!)).toMatchObject({
      billingRates: [
        {
          amount: '25.00',
          basis: 'hour',
          id: 'standard',
          name: 'Updated rate',
        },
      ],
      futureSetting: { retained: true },
      openingHours: { monday: [{ end: '17:00', start: '09:00' }] },
    });
    expect(requests[0]).not.toHaveProperty('formJson');
    expect(await screen.findByText('Resource saved.')).toBeInTheDocument();
  });

  it('deactivates and reactivates a resource without replacing its configuration', async () => {
    mockAccess();
    const saved = mockResources();
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.put('/api/teams/:teamSlug/resources/:id', async ({ request }) => {
        const body = (await request.json()) as SaveResourceRequest;
        requests.push(body);
        const previous = saved.get(1)!;
        const updated = resource({
          ...previous,
          ...body,
          spaceId: previous.spaceId,
          updatedAt: `2026-10-0${requests.length + 1}T12:00:00Z`,
        });
        saved.set(1, updated);
        return HttpResponse.json(updated);
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
    fireEvent.click(
      await screen.findByRole('checkbox', { name: 'Active resource' })
    );
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('Resource saved.')).toBeInTheDocument();
    expect(requests[0]).toMatchObject({
      expectedConfigId: 10,
      isActive: false,
      updatedAt: resource().updatedAt,
    });
    expect(requests[0]).not.toHaveProperty('formJson');
    expect(requests[0]).not.toHaveProperty('detailsJson');
    fireEvent.click(screen.getByRole('checkbox', { name: 'Active resource' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]).toMatchObject({
      expectedConfigId: 10,
      isActive: true,
      updatedAt: '2026-10-02T12:00:00Z',
    });
    expect(await screen.findByText('Resource saved.')).toBeInTheDocument();
    expect(
      screen.getByRole('checkbox', { name: 'Active resource' })
    ).toBeChecked();
  });

  it('allows metadata changes while preserving unsupported configuration', async () => {
    mockAccess();
    const original = resource();
    original.config = {
      ...original.config!,
      detailsJson: '{"futureSettings":true}',
      detailsSchemaVersion: 2,
      formJson: '{invalid',
    };
    const saved = mockResources([original]);
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.put('/api/teams/:teamSlug/resources/:id', async ({ request }) => {
        const body = (await request.json()) as SaveResourceRequest;
        requests.push(body);
        const updated = {
          ...original,
          ...body,
          spaceId: original.spaceId,
          updatedAt: '2026-10-02T12:00:00Z',
        };
        saved.set(1, updated);
        return HttpResponse.json(updated);
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Resource name' }),
      { target: { value: 'Renamed microscope' } }
    );
    expect(
      screen.getByText(/saved form is not valid JSON/)
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Hours and rates' }));
    expect(
      screen.getByText(/These settings use a newer format/)
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('checkbox', { name: 'Include default opening hours' })
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      expectedConfigId: 10,
      name: 'Renamed microscope',
    });
    expect(requests[0]).not.toHaveProperty('formJson');
    expect(requests[0]).not.toHaveProperty('formSchemaVersion');
    expect(requests[0]).not.toHaveProperty('detailsJson');
    expect(requests[0]).not.toHaveProperty('detailsSchemaVersion');
    expect(await screen.findByText('Resource saved.')).toBeInTheDocument();
    expect(saved.get(1)!.config).toEqual(original.config);
  });

  it('can deactivate legacy configurations without changing their empty form or invalid settings', async () => {
    mockAccess();
    const original = resource();
    original.config = {
      ...original.config!,
      detailsJson: JSON.stringify({
        billingRates: [
          { amount: 'unknown', basis: 'hour', id: 'legacy', name: '' },
        ],
        openingHours: { monday: [{ end: '08:00', start: '09:00' }] },
        schemaVersion: 1,
      }),
      formJson: '{"fields":[]}',
    };
    const saved = mockResources([original]);
    const requests: SaveResourceRequest[] = [];
    server.use(
      http.put('/api/teams/:teamSlug/resources/:id', async ({ request }) => {
        const body = (await request.json()) as SaveResourceRequest;
        requests.push(body);
        const updated = {
          ...original,
          ...body,
          spaceId: original.spaceId,
          updatedAt: '2026-10-02T12:00:00Z',
        };
        saved.set(1, updated);
        return HttpResponse.json(updated);
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
    fireEvent.click(
      await screen.findByRole('checkbox', { name: 'Active resource' })
    );
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      expectedConfigId: 10,
      isActive: false,
    });
    expect(requests[0]).not.toHaveProperty('formJson');
    expect(requests[0]).not.toHaveProperty('detailsJson');
    expect(await screen.findByText('Resource saved.')).toBeInTheDocument();
    expect(saved.get(1)!.config).toEqual(original.config);
  });
});
