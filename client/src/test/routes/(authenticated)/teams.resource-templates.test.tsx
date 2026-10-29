import { QueryClient } from '@tanstack/react-query';
import { createBrowserHistory } from '@tanstack/react-router';
import {
  act,
  fireEvent,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ResourceTemplate } from '@/features/resource-templates/models/ResourceTemplate.ts';
import type { SaveResourceTemplateRequest } from '@/features/resource-templates/models/SaveResourceTemplateRequest.ts';
import {
  resourceTemplateQueryOptions,
  resourceTemplatesQueryOptions,
} from '@/queries/resourceTemplates.ts';
import type { User } from '@/queries/user.ts';
import { server } from '@/test/mswUtils.ts';
import { renderRoute } from '@/test/routerUtils.tsx';

const user: User = {
  email: 'editor@example.com',
  iamId: '100001',
  id: '1',
  isSiteAdmin: false,
  name: 'Taylor Editor',
  roles: [],
};
const team = {
  id: 1,
  name: 'Plant Sciences',
  roles: ['editor'],
  slug: 'plant-sciences',
};
const otherTeam = {
  id: 2,
  name: 'Animal Science',
  roles: ['editor'],
  slug: 'animal-science',
};
const listPath = '/teams/plant-sciences/resource-templates';
const formJson = JSON.stringify({
  fields: [{ id: 'name', label: 'Visitor name', type: 'input' }],
});
const resourceDefaultsJson = JSON.stringify({
  billingRates: [
    {
      amount: '25.00',
      basis: 'hour',
      currency: 'USD',
      id: 'fb7a388a-ef81-4cc0-aec2-f8772796eb42',
      name: 'Standard',
    },
  ],
  openingHours: { monday: [{ end: '17:00', start: '09:00' }] },
  schemaVersion: 1,
});

function makeTemplate(
  overrides: Partial<ResourceTemplate> = {}
): ResourceTemplate {
  return {
    createdAt: '2026-10-01T12:00:00Z',
    description: 'A reusable booking form.',
    formJson,
    formSchemaVersion: 1,
    id: 1,
    isActive: true,
    name: 'Team equipment',
    resourceDefaultsJson: null,
    teamId: team.id,
    updatedAt: '2026-10-01T12:00:00Z',
    ...overrides,
  };
}

let cleanup: (() => void) | undefined;

afterEach(() => {
  cleanup?.();
  cleanup = undefined;
  vi.restoreAllMocks();
});

function mockAccess({
  isSiteAdmin = false,
  role = 'editor',
}: { isSiteAdmin?: boolean; role?: string | null } = {}) {
  server.use(
    http.get('/api/user/me', () => HttpResponse.json({ ...user, isSiteAdmin })),
    http.get('/api/teams', () => HttpResponse.json([team, otherTeam])),
    http.get('/api/teams/:teamSlug', ({ params }) =>
      HttpResponse.json({
        isSiteAdmin,
        roles: role ? [role] : [],
        team: params.teamSlug === otherTeam.slug ? otherTeam : team,
      })
    ),
    http.get('/api/antiforgery', () =>
      HttpResponse.json({
        formFieldName: '__RequestVerificationToken',
        requestToken: 'team-template-token',
      })
    )
  );
}

function mockTemplates(initial: ResourceTemplate[] = [makeTemplate()]) {
  const templates = new Map(initial.map((template) => [template.id, template]));
  server.use(
    http.get('/api/teams/:teamSlug/resource-templates', ({ params }) => {
      const teamId =
        params.teamSlug === otherTeam.slug ? otherTeam.id : team.id;
      return HttpResponse.json(
        [...templates.values()].filter(
          (template) =>
            template.teamId === teamId ||
            (template.teamId === null && template.isActive)
        )
      );
    }),
    http.get('/api/teams/:teamSlug/resource-templates/:id', ({ params }) => {
      const teamId =
        params.teamSlug === otherTeam.slug ? otherTeam.id : team.id;
      const template = templates.get(Number(params.id));
      return template &&
        (template.teamId === teamId ||
          (template.teamId === null && template.isActive))
        ? HttpResponse.json(template)
        : new HttpResponse(null, { status: 404 });
    })
  );
  return templates;
}

function silenceRouteErrors() {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(console, 'warn').mockImplementation(() => undefined);
}

describe('team resource templates', () => {
  it.each([
    { isSiteAdmin: false, role: 'editor' },
    { isSiteAdmin: false, role: 'admin' },
    { isSiteAdmin: true, role: null },
  ])(
    'allows template management for $role, site admin $isSiteAdmin',
    async (access) => {
      mockAccess(access);
      mockTemplates();
      ({ cleanup } = renderRoute({ initialPath: listPath }));

      expect(
        await screen.findByRole('row', { name: /Team equipment/ })
      ).toBeInTheDocument();
      expect(
        within(
          screen.getByRole('navigation', { name: 'Team administration' })
        ).getByRole('link', { name: 'Resource templates' })
      ).toHaveAttribute('href', listPath);
      expect(
        screen.getByRole('link', { name: 'Create template' })
      ).toHaveAttribute('href', `${listPath}/new`);
      expect(
        screen.getByRole('link', { name: 'Edit Team equipment' })
      ).toBeInTheDocument();
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
    'hides template navigation and denies direct %s access to %s',
    async (role, suffix) => {
      silenceRouteErrors();
      mockAccess({ role });
      const denied = vi.fn(() => new HttpResponse(null, { status: 403 }));
      server.use(
        http.get('/api/teams/:teamSlug/resource-templates', denied),
        http.get('/api/teams/:teamSlug/resource-templates/:id', denied)
      );
      const rendered = renderRoute({ initialPath: '/teams/plant-sciences' });
      cleanup = rendered.cleanup;
      await screen.findByRole('heading', { name: 'Team overview' });
      expect(
        screen.queryByRole('link', { name: 'Resource templates' })
      ).not.toBeInTheDocument();

      await act(async () => {
        await rendered.router.navigate({ to: `${listPath}${suffix}` });
      });

      expect(
        await screen.findByRole('heading', { name: 'Not authorized' })
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('textbox', { name: 'Template name' })
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('link', { name: 'Create template' })
      ).not.toBeInTheDocument();
    }
  );

  it('keeps system and team description tooltips associated with their own names', async () => {
    mockAccess();
    mockTemplates([
      makeTemplate({ description: 'Equipment reserved by the team.' }),
      makeTemplate({
        description: 'A system form available to all teams.',
        id: 2,
        name: 'System equipment',
        teamId: null,
      }),
    ]);
    ({ cleanup } = renderRoute({ initialPath: listPath }));
    const teamName = await screen.findByRole('link', {
      name: 'Team equipment',
    });
    const systemName = screen.getByRole('link', { name: 'System equipment' });
    expect(teamName).toHaveAccessibleDescription(
      'Equipment reserved by the team.'
    );
    expect(systemName).toHaveAccessibleDescription(
      'A system form available to all teams.'
    );
    expect(teamName.getAttribute('aria-describedby')).not.toBe(
      systemName.getAttribute('aria-describedby')
    );

    fireEvent.mouseEnter(teamName);
    expect(screen.getByRole('tooltip')).toHaveTextContent(
      'Equipment reserved by the team.'
    );
    fireEvent.mouseLeave(teamName, { relatedTarget: document.body });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    act(() => systemName.focus());
    expect(screen.getByRole('tooltip')).toHaveTextContent(
      'A system form available to all teams.'
    );
    expect(
      screen.getByRole('link', { name: 'System equipment' })
    ).toHaveFocus();
    fireEvent.keyDown(systemName, { key: 'Escape' });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    expect(systemName).toHaveFocus();
    expect(systemName).toHaveAccessibleDescription(
      'A system form available to all teams.'
    );
  });

  it('offers View and Edit for an active team template and switches from read-only view to edit', async () => {
    mockAccess();
    mockTemplates([makeTemplate({ resourceDefaultsJson })]);
    const rendered = renderRoute({ initialPath: listPath });
    cleanup = rendered.cleanup;
    const row = within(
      await screen.findByRole('row', { name: /Team equipment/ })
    );
    expect(
      row.getByRole('link', { name: 'Edit Team equipment' })
    ).toHaveAttribute('href', `${listPath}/1`);
    const view = row.getByRole('link', { name: 'View Team equipment' });
    expect(view).toHaveAttribute('href', `${listPath}/1?view=true`);
    fireEvent.click(view);

    expect(
      await screen.findByRole('heading', { name: 'View resource template' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('textbox', { name: 'Template name' })
    ).toBeDisabled();
    expect(
      screen.getByRole('checkbox', { name: 'Active template' })
    ).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: 'Save template' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('tab', { name: 'Build form' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Add Input' })
    ).not.toBeInTheDocument();
    const answer = screen.getByRole('textbox', { name: /Visitor name/ });
    fireEvent.change(answer, { target: { value: 'Taylor' } });
    expect(answer).toHaveValue('Taylor');
    fireEvent.click(screen.getByRole('tab', { name: 'Resource defaults' }));
    expect(screen.getByLabelText('Monday opening time 1')).toHaveValue('09:00');
    expect(screen.getByLabelText('Monday opening time 1')).toBeDisabled();
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toHaveValue(
      '25.00'
    );
    expect(
      screen.getByRole('textbox', { name: 'Amount (USD) 1' })
    ).toBeDisabled();
    fireEvent.click(screen.getByRole('link', { name: 'Edit template' }));

    expect(
      await screen.findByRole('heading', { name: 'Edit resource template' })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.search).not.toHaveProperty(
      'view',
      true
    );
    expect(
      screen.getByRole('textbox', { name: 'Template name' })
    ).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Save template' })).toBeEnabled();
    expect(screen.getByRole('tab', { name: 'Build form' })).toBeEnabled();
  });

  it('shows active system templates and reveals older team versions without inactive system templates', async () => {
    mockAccess();
    mockTemplates([
      makeTemplate(),
      makeTemplate({ id: 2, name: 'System equipment', teamId: null }),
      makeTemplate({ id: 3, isActive: false, name: 'Old team equipment' }),
      makeTemplate({
        id: 4,
        isActive: false,
        name: 'Retired system equipment',
        teamId: null,
      }),
      makeTemplate({
        id: 5,
        name: 'Other team equipment',
        teamId: otherTeam.id,
      }),
    ]);
    ({ cleanup } = renderRoute({ initialPath: listPath }));

    const systemRow = within(
      await screen.findByRole('row', { name: /System equipment/ })
    );
    expect(
      systemRow.getByRole('link', { name: 'View System equipment' })
    ).toHaveAttribute('href', `${listPath}/2?view=true`);
    expect(
      systemRow.queryByRole('link', { name: /Edit/ })
    ).not.toBeInTheDocument();
    expect(
      systemRow.getByRole('button', {
        name: 'Duplicate System equipment into team',
      })
    ).toBeEnabled();
    expect(
      screen.queryByRole('row', { name: /Old team equipment/ })
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: /Include archived/ }));

    expect(
      screen.getByRole('link', { name: 'View Old team equipment' })
    ).toHaveAttribute('href', `${listPath}/3?view=true`);
    expect(
      screen.queryByRole('link', { name: 'Edit Old team equipment' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('row', {
        name: /Retired system equipment|Other team equipment/,
      })
    ).not.toBeInTheDocument();
  });

  it('views a system template read-only with preview and defaults, then duplicates it into the team', async () => {
    mockAccess();
    const original = makeTemplate({
      id: 2,
      name: 'System equipment',
      resourceDefaultsJson,
      teamId: null,
    });
    const templates = mockTemplates([original]);
    let token: string | null = null;
    server.use(
      http.post(
        '/api/teams/plant-sciences/resource-templates/2/duplicate',
        ({ request }) => {
          token = request.headers.get('RequestVerificationToken');
          const copy = makeTemplate({
            ...original,
            id: 3,
            name: 'System equipment (copy)',
            teamId: team.id,
          });
          templates.set(copy.id, copy);
          return HttpResponse.json(copy, { status: 201 });
        }
      )
    );
    const rendered = renderRoute({ initialPath: `${listPath}/2` });
    cleanup = rendered.cleanup;

    expect(
      await screen.findByRole('heading', { name: 'View resource template' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('textbox', { name: 'Template name' })
    ).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: 'Save template' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: 'Edit template' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Add Input' })
    ).not.toBeInTheDocument();
    const answer = screen.getByRole('textbox', { name: /Visitor name/ });
    fireEvent.change(answer, { target: { value: 'Taylor' } });
    expect(answer).toHaveValue('Taylor');
    fireEvent.click(screen.getByRole('tab', { name: 'Resource defaults' }));
    expect(screen.getByLabelText('Monday opening time 1')).toHaveValue('09:00');
    expect(screen.getByLabelText('Monday opening time 1')).toBeDisabled();
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toHaveValue(
      '25.00'
    );
    expect(
      screen.getByRole('textbox', { name: 'Amount (USD) 1' })
    ).toBeDisabled();
    fireEvent.click(
      screen.getByRole('link', { name: 'Back to resource templates' })
    );
    fireEvent.click(
      await screen.findByRole('button', {
        name: 'Duplicate System equipment into team',
      })
    );

    expect(
      await screen.findByRole('heading', { name: 'Edit resource template' })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe(`${listPath}/3`);
    expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
      'System equipment (copy)'
    );
    expect(screen.getByRole('button', { name: 'Save template' })).toBeEnabled();
    expect(token).toBe('team-template-token');
    expect(templates.get(2)).toEqual(original);
    expect(templates.get(3)).toMatchObject({
      formJson,
      resourceDefaultsJson,
      teamId: team.id,
    });
  });

  it('hides a cached system template when it becomes unavailable on reopening', async () => {
    mockAccess();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(
      resourceTemplateQueryOptions(2, team.slug).queryKey,
      makeTemplate({ id: 2, name: 'Retired system equipment', teamId: null })
    );
    server.use(
      http.get(
        '/api/teams/plant-sciences/resource-templates/2',
        () => new HttpResponse(null, { status: 404 })
      )
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/2`, queryClient }));

    expect(
      await screen.findByRole('heading', { name: 'Template not found' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('textbox', { name: 'Template name' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('textbox', { name: /Visitor name/ })
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Back to resource templates' })
    ).toHaveAttribute('href', listPath);
  });

  it('waits for fresh data before reopening a cached team template that was archived', async () => {
    mockAccess();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(
      resourceTemplateQueryOptions(1, team.slug).queryKey,
      makeTemplate()
    );
    const response = Promise.withResolvers<void>();
    server.use(
      http.get('/api/teams/plant-sciences/resource-templates/1', async () => {
        await response.promise;
        return HttpResponse.json(
          makeTemplate({ isActive: false, name: 'Archived team equipment' })
        );
      })
    );
    ({ cleanup } = renderRoute({ initialPath: `${listPath}/1`, queryClient }));

    try {
      expect(await screen.findByText('Loading template…')).toBeInTheDocument();
      expect(
        screen.queryByRole('textbox', { name: 'Template name' })
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: 'Save template' })
      ).not.toBeInTheDocument();
    } finally {
      response.resolve();
    }

    expect(
      await screen.findByRole('heading', { name: 'View resource template' })
    ).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
      'Archived team equipment'
    );
    expect(
      screen.getByRole('textbox', { name: 'Template name' })
    ).toBeDisabled();
    expect(
      screen.getByRole('checkbox', { name: 'Active template' })
    ).not.toBeChecked();
    expect(
      screen.queryByRole('button', { name: 'Save template' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Add Input' })
    ).not.toBeInTheDocument();
  });

  it('creates a team template through the shared editor and caches it within the team', async () => {
    mockAccess();
    const templates = mockTemplates([]);
    let saved: SaveResourceTemplateRequest | undefined;
    let token: string | null = null;
    server.use(
      http.post(
        '/api/teams/plant-sciences/resource-templates',
        async ({ request }) => {
          saved = (await request.json()) as SaveResourceTemplateRequest;
          token = request.headers.get('RequestVerificationToken');
          const template = makeTemplate({ ...saved, id: 10 });
          templates.set(template.id, template);
          return HttpResponse.json(template, { status: 201 });
        }
      )
    );
    const rendered = renderRoute({ initialPath: listPath });
    cleanup = rendered.cleanup;
    fireEvent.click(
      await screen.findByRole('link', { name: 'Create template' })
    );
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Camera checkout' } }
    );
    fireEvent.click(screen.getByRole('button', { name: 'Add Input' }));
    fireEvent.click(screen.getByRole('button', { name: 'Create template' }));

    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(`${listPath}/10`)
    );
    expect(
      await screen.findByRole('textbox', { name: 'Template name' })
    ).toHaveValue('Camera checkout');
    expect(saved?.name).toBe('Camera checkout');
    expect(token).toBe('team-template-token');
    expect(
      rendered.queryClient.getQueryData(
        resourceTemplateQueryOptions(10, team.slug).queryKey
      )
    ).toMatchObject({ id: 10, teamId: team.id });
    expect(
      rendered.queryClient.getQueryData(
        resourceTemplateQueryOptions(10).queryKey
      )
    ).toBeUndefined();
    expect(
      rendered.queryClient.getQueryData(
        resourceTemplateQueryOptions(10, otherTeam.slug).queryKey
      )
    ).toBeUndefined();
  });

  it('opens an older team version read-only when returning after a versioned save', async () => {
    mockAccess();
    const original = makeTemplate();
    const templates = mockTemplates([original]);
    let saved: SaveResourceTemplateRequest | undefined;
    server.use(
      http.put(
        '/api/teams/plant-sciences/resource-templates/1',
        async ({ request }) => {
          saved = (await request.json()) as SaveResourceTemplateRequest;
          const updatedAt = '2026-10-02T12:00:00Z';
          templates.set(1, { ...original, isActive: false, updatedAt });
          const revision = makeTemplate({
            ...saved,
            formSchemaVersion: 2,
            id: 2,
            updatedAt,
          });
          templates.set(2, revision);
          return HttpResponse.json(revision);
        }
      )
    );
    const rendered = renderRoute({ initialPath: listPath });
    cleanup = rendered.cleanup;
    fireEvent.click(
      await screen.findByRole('link', { name: 'Edit Team equipment' })
    );
    fireEvent.change(await screen.findByRole('textbox', { name: 'Label' }), {
      target: { value: 'Contact name' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(`${listPath}/2`)
    );
    expect(screen.getByText('Version 2')).toBeInTheDocument();
    expect(saved).toMatchObject({
      formSchemaVersion: 1,
      updatedAt: original.updatedAt,
    });
    await act(async () => rendered.router.history.back());
    fireEvent.click(
      await screen.findByRole('checkbox', { name: /Include archived/ })
    );
    const archivedRow = within(
      await screen.findByRole('row', { name: /Team equipment.*Archived/ })
    );
    fireEvent.click(
      archivedRow.getByRole('link', { name: 'View Team equipment' })
    );

    expect(
      await screen.findByRole('heading', { name: 'View resource template' })
    ).toBeInTheDocument();
    expect(screen.getByText('Version 1')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /Visitor name/ })).toBeEnabled();
    expect(
      screen.getByRole('textbox', { name: 'Template name' })
    ).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: 'Save template' })
    ).not.toBeInTheDocument();
    expect(templates.get(1)?.formJson).toBe(original.formJson);
  });

  it.each([409, 500])(
    'preserves the team draft after a failed save (%s)',
    async (status) => {
      mockAccess();
      mockTemplates();
      server.use(
        http.put(
          '/api/teams/plant-sciences/resource-templates/1',
          () => new HttpResponse(null, { status })
        )
      );
      ({ cleanup } = renderRoute({ initialPath: `${listPath}/1` }));
      fireEvent.change(
        await screen.findByRole('textbox', { name: 'Template name' }),
        { target: { value: 'Unfinished team draft' } }
      );
      fireEvent.change(screen.getByRole('textbox', { name: 'Label' }), {
        target: { value: 'Contact name' },
      });
      fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(
        status === 409 ? /changed|archived/i : /could not save/i
      );
      expect(
        screen.getByRole('textbox', { name: 'Template name' })
      ).toHaveValue('Unfinished team draft');
      expect(screen.getByRole('textbox', { name: 'Label' })).toHaveValue(
        'Contact name'
      );
    }
  );

  it('rechecks cached list access before allowing a direct link', async () => {
    silenceRouteErrors();
    mockAccess();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(
      resourceTemplatesQueryOptions(team.slug).queryKey,
      [makeTemplate()]
    );
    server.use(
      http.get(
        '/api/teams/plant-sciences/resource-templates',
        () => new HttpResponse(null, { status: 403 })
      )
    );
    ({ cleanup } = renderRoute({ initialPath: listPath, queryClient }));

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('row', { name: /Team equipment/ })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: 'Create template' })
    ).not.toBeInTheDocument();
  });

  it('removes cached templates and management controls after access is revoked', async () => {
    mockAccess();
    mockTemplates();
    const rendered = renderRoute({ initialPath: listPath });
    cleanup = rendered.cleanup;
    await screen.findByRole('row', { name: /Team equipment/ });
    server.use(
      http.get(
        '/api/teams/plant-sciences/resource-templates',
        () => new HttpResponse(null, { status: 403 })
      )
    );

    await act(async () => {
      await rendered.queryClient.invalidateQueries({
        queryKey: resourceTemplatesQueryOptions(team.slug).queryKey,
      });
    });

    expect(
      await screen.findByRole('heading', { name: 'Not authorized' })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('row', { name: /Team equipment/ })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: 'Create template' })
    ).not.toBeInTheDocument();
  });

  it('protects an unsaved new template and resets it when changing teams after discarding', async () => {
    mockAccess({ isSiteAdmin: true, role: null });
    mockTemplates([]);
    const previousUrl = window.location.href;
    const previousState = window.history.state;
    window.history.replaceState(null, '', `${listPath}/new`);
    const history = createBrowserHistory();
    const rendered = renderRoute({ history });
    cleanup = () => {
      rendered.cleanup();
      history.destroy();
      window.history.replaceState(previousState, '', previousUrl);
    };
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Plant draft' } }
    );
    fireEvent.click(screen.getByRole('button', { name: 'Add Input' }));
    act(() => {
      void rendered.router.navigate({
        params: { teamSlug: otherTeam.slug },
        to: '/teams/$teamSlug/resource-templates/new',
      });
    });
    fireEvent.click(
      await screen.findByRole('button', { name: 'Keep editing' })
    );
    expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
      'Plant draft'
    );
    expect(rendered.router.state.location.pathname).toBe(`${listPath}/new`);
    act(() => {
      void rendered.router.navigate({
        params: { teamSlug: otherTeam.slug },
        to: '/teams/$teamSlug/resource-templates/new',
      });
    });
    fireEvent.click(
      await screen.findByRole('button', { name: 'Discard changes' })
    );

    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/teams/animal-science/resource-templates/new'
      )
    );
    expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
      ''
    );
    expect(
      screen.queryByRole('textbox', { name: 'Label' })
    ).not.toBeInTheDocument();
  });
});
