import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { FormDefinition } from '@/features/resource-templates/models/FormDefinition.ts';
import type { ResourceTemplate } from '@/features/resource-templates/models/ResourceTemplate.ts';
import type { SaveResourceTemplateRequest } from '@/features/resource-templates/models/SaveResourceTemplateRequest.ts';
import { resourceTemplateQueryOptions } from '@/queries/resourceTemplates.ts';
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

const definition: FormDefinition = {
  fields: [
    {
      id: 'name',
      label: 'Visitor name',
      type: 'input',
      validation: { maxLength: 40, required: true },
    },
    { id: 'purpose', label: 'Purpose', type: 'textarea' },
    {
      id: 'equipment',
      label: 'Equipment',
      options: [{ id: 'camera', label: 'Camera' }],
      type: 'checkboxes',
      validation: { required: true },
    },
    {
      id: 'location',
      label: 'Location',
      options: [{ id: 'campus', label: 'Campus' }],
      type: 'dropdown',
    },
    {
      id: 'duration',
      label: 'Duration',
      options: [{ id: 'day', label: 'Full day' }],
      type: 'radio',
    },
    { id: 'instructions', label: 'Bring your campus ID.', type: 'text' },
  ],
};

function makeTemplate(
  overrides: Partial<ResourceTemplate> = {}
): ResourceTemplate {
  return {
    createdAt: '2026-10-01T12:00:00Z',
    formJson: JSON.stringify(definition),
    formSchemaVersion: 1,
    id: 1,
    isActive: true,
    name: 'Equipment booking',
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

function mockAdminAccess() {
  server.use(
    http.get('/api/user/me', () => HttpResponse.json(siteAdmin)),
    http.get('/api/admin/access', () => new HttpResponse(null, { status: 204 })),
    http.get('/api/admin/antiforgery', () =>
      HttpResponse.json({ token: 'resource-template-test-token' })
    )
  );
}

function mockTemplateStore(initial: ResourceTemplate[] = [makeTemplate()]) {
  const templates = new Map(initial.map((template) => [template.id, template]));
  const saves: {
    body: SaveResourceTemplateRequest;
    id: number;
    token: string | null;
  }[] = [];
  server.use(
    http.get('/api/admin/resource-templates', () =>
      HttpResponse.json([...templates.values()])
    ),
    http.get('/api/admin/resource-templates/:id', ({ params }) => {
      const template = templates.get(Number(params.id));
      return template
        ? HttpResponse.json(template)
        : new HttpResponse(null, { status: 404 });
    }),
    http.post('/api/admin/resource-templates', async ({ request }) => {
      const body = (await request.json()) as SaveResourceTemplateRequest;
      const id = Math.max(0, ...templates.keys()) + 1;
      const template = makeTemplate({ ...body, id });
      saves.push({
        body,
        id,
        token: request.headers.get('RequestVerificationToken'),
      });
      templates.set(id, template);
      return HttpResponse.json(template, { status: 201 });
    }),
    http.put('/api/admin/resource-templates/:id', async ({ params, request }) => {
      const id = Number(params.id);
      const body = (await request.json()) as SaveResourceTemplateRequest;
      const template = makeTemplate({ ...body, id });
      saves.push({
        body,
        id,
        token: request.headers.get('RequestVerificationToken'),
      });
      templates.set(id, template);
      return HttpResponse.json(template);
    })
  );
  return { saves, templates };
}

describe('site admin resource templates', () => {
  it('creates a template from the admin list and opens the saved form', async () => {
    mockAdminAccess();
    const { saves, templates } = mockTemplateStore([]);
    const rendered = renderRoute({ initialPath: '/admin/resource-templates' });
    cleanup = rendered.cleanup;

    fireEvent.click(await screen.findByRole('link', { name: 'Create template' }));
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Camera checkout' } }
    );
    fireEvent.click(screen.getByRole('button', { name: 'Add Input' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Label' }), {
      target: { value: 'Contact name' },
    });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Required' }));
    fireEvent.click(screen.getByRole('button', { name: 'Create template' }));

    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/1'
      )
    );
    expect(
      await screen.findByRole('textbox', { name: 'Template name' })
    ).toHaveValue('Camera checkout');
    expect(saves).toHaveLength(1);
    expect(saves[0]).toMatchObject({
      body: { formSchemaVersion: 1, isActive: true, name: 'Camera checkout' },
      id: 1,
      token: 'resource-template-test-token',
    });
    expect(JSON.parse(templates.get(1)!.formJson).fields).toEqual([
      expect.objectContaining({
        label: 'Contact name',
        type: 'input',
        validation: { required: true },
      }),
    ]);
  });

  it('saves and reloads the ordered form without losing field types, IDs, choices, or validation', async () => {
    mockAdminAccess();
    const { saves, templates } = mockTemplateStore();
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));

    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Updated equipment booking' } }
    );
    fireEvent.click(screen.getByRole('button', { name: 'Move Visitor name down' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(await screen.findByText(/Template saved/)).toBeInTheDocument();

    const expected = {
      fields: [
        definition.fields[1],
        definition.fields[0],
        ...definition.fields.slice(2),
      ],
    };
    expect(saves).toHaveLength(1);
    expect(JSON.parse(templates.get(1)!.formJson)).toEqual(expected);
    expect(saves[0].token).toBe('resource-template-test-token');
    cleanup?.();
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
    expect(
      await screen.findByRole('textbox', { name: 'Template name' })
    ).toHaveValue('Updated equipment booking');
    fireEvent.click(screen.getByRole('button', { name: 'Preview form' }));
    expect(screen.getByRole('textbox', { name: /Visitor name/ })).toHaveAttribute(
      'maxLength',
      '40'
    );
    expect(screen.getByRole('textbox', { name: /Purpose/ })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Camera' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /Location/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Full day' })).toBeInTheDocument();
    expect(screen.getByText('Bring your campus ID.')).toBeInTheDocument();
  });

  it('duplicates a template and saves edits to the independent copy', async () => {
    mockAdminAccess();
    const original = makeTemplate();
    const { saves, templates } = mockTemplateStore([original]);
    let duplicateToken: string | null = null;
    server.use(
      http.post('/api/admin/resource-templates/1/duplicate', ({ request }) => {
        duplicateToken = request.headers.get('RequestVerificationToken');
        const copy = makeTemplate({ id: 2, name: 'Equipment booking (copy)' });
        templates.set(copy.id, copy);
        return HttpResponse.json(copy, { status: 201 });
      })
    );
    const rendered = renderRoute({ initialPath: '/admin/resource-templates' });
    cleanup = rendered.cleanup;

    const row = await screen.findByRole('row', { name: /Equipment booking/ });
    fireEvent.click(within(row).getByRole('button', { name: /Duplicate/ }));
    expect(
      await screen.findByRole('textbox', { name: 'Template name' })
    ).toHaveValue('Equipment booking (copy)');
    expect(rendered.router.state.location.pathname).toBe(
      '/admin/resource-templates/2'
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Template name' }), {
      target: { value: 'Camera booking' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Move Visitor name down' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(await screen.findByText(/Template saved/)).toBeInTheDocument();

    expect(duplicateToken).toBe('resource-template-test-token');
    expect(saves[0].id).toBe(2);
    expect(templates.get(1)).toEqual(original);
    expect(templates.get(2)!.formJson).not.toBe(original.formJson);
  });

  it('archives and restores a template only when the changes are saved', async () => {
    mockAdminAccess();
    const { saves, templates } = mockTemplateStore();
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
    const active = await screen.findByRole('checkbox', { name: 'Active template' });

    fireEvent.click(active);
    expect(templates.get(1)!.isActive).toBe(true);
    expect(saves).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(await screen.findByText(/Template saved/)).toBeInTheDocument();
    expect(templates.get(1)!.isActive).toBe(false);

    fireEvent.click(active);
    expect(templates.get(1)!.isActive).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() => expect(saves).toHaveLength(2));
    expect(templates.get(1)!.isActive).toBe(true);
    expect(JSON.parse(templates.get(1)!.formJson)).toEqual(definition);
  });

  it('retains unsaved edits after a server failure and allows retrying', async () => {
    mockAdminAccess();
    const { saves } = mockTemplateStore();
    server.use(
      http.put(
        '/api/admin/resource-templates/1',
        () => new HttpResponse(null, { status: 500 }),
        { once: true }
      )
    );
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));

    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'My unsaved changes' } }
    );
    fireEvent.click(screen.getByRole('button', { name: 'Move Visitor name down' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
      'My unsaved changes'
    );
    expect(saves).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

    expect(await screen.findByText(/Template saved/)).toBeInTheDocument();
    expect(saves[0].body.name).toBe('My unsaved changes');
    expect(JSON.parse(saves[0].body.formJson).fields[0].id).toBe('purpose');
  });

  it('blocks invalid length rules before sending changes to the server', async () => {
    mockAdminAccess();
    const { saves } = mockTemplateStore();
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));

    fireEvent.change(
      await screen.findByRole('spinbutton', { name: 'Minimum length' }),
      { target: { value: '50' } }
    );
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(
      await screen.findByText(
        /Field 1: Minimum length cannot exceed maximum length/
      )
    ).toBeInTheDocument();
    expect(saves).toHaveLength(0);
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Minimum length' }), {
      target: { value: '5' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

    expect(await screen.findByText(/Template saved/)).toBeInTheDocument();
    expect(JSON.parse(saves[0].body.formJson).fields[0].validation).toEqual({
      maxLength: 40,
      minLength: 5,
      required: true,
    });
  });

  it('preserves an open draft when a background refresh fails', async () => {
    mockAdminAccess();
    mockTemplateStore();
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Unfinished draft' } }
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Label' }), {
      target: { value: 'Contact name' },
    });
    server.use(
      http.get(
        '/api/admin/resource-templates/1',
        () => new HttpResponse(null, { status: 404 })
      )
    );

    const queryKey = resourceTemplateQueryOptions(1).queryKey;
    await act(async () => {
      await rendered.queryClient.refetchQueries({ exact: true, queryKey });
    });

    expect(rendered.queryClient.getQueryState(queryKey)?.status).toBe('error');
    expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
      'Unfinished draft'
    );
    expect(screen.getByRole('textbox', { name: 'Label' })).toHaveValue(
      'Contact name'
    );
    expect(screen.getByRole('button', { name: 'Save template' })).toBeEnabled();
    expect(
      screen.queryByRole('heading', { name: 'Template not found' })
    ).not.toBeInTheDocument();
  });

  it('preserves an open draft when the query cache receives a newer unsupported form schema', async () => {
    mockAdminAccess();
    mockTemplateStore();
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Unfinished draft' } }
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Label' }), {
      target: { value: 'Contact name' },
    });

    await act(async () => {
      rendered.queryClient.setQueryData(
        resourceTemplateQueryOptions(1).queryKey,
        makeTemplate({ formSchemaVersion: 2, name: 'External update' })
      );
    });

    expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
      'Unfinished draft'
    );
    expect(screen.getByRole('textbox', { name: 'Label' })).toHaveValue(
      'Contact name'
    );
    expect(screen.getByRole('button', { name: 'Save template' })).toBeEnabled();
    expect(
      screen.queryByText('This template cannot be edited with this builder')
    ).not.toBeInTheDocument();
  });

  it('protects unsaved edits when navigating away and allows explicitly discarding them', async () => {
    mockAdminAccess();
    mockTemplateStore();
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Unfinished draft' } }
    );
    fireEvent.click(screen.getByRole('link', { name: 'Resource templates' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Keep editing' }));

    expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
      'Unfinished draft'
    );
    expect(rendered.router.state.location.pathname).toBe(
      '/admin/resource-templates/1'
    );
    fireEvent.click(screen.getByRole('link', { name: 'Resource templates' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Discard changes' }));

    expect(
      await screen.findByRole('heading', { name: 'Resource templates' })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toMatch(
      /^\/admin\/resource-templates\/?$/
    );
  });

  it.each([
    { formJson: '{"fields":[]}', formSchemaVersion: 2 },
    { formJson: 'invalid-json', formSchemaVersion: 1 },
    { formJson: '{"fields":[],"futureFeature":true}', formSchemaVersion: 1 },
  ])(
    'prevents overwriting unsupported or invalid saved forms ($formJson, version $formSchemaVersion)',
    async (savedForm) => {
      mockAdminAccess();
      const original = makeTemplate(savedForm);
      const { saves, templates } = mockTemplateStore([original]);
      ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(
        /not supported|cannot be edited safely/
      );
      const save = screen.queryByRole('button', { name: 'Save template' });
      if (save) {
        expect(save).toBeDisabled();
      }
      expect(
        screen.queryByRole('button', { name: 'Add Input' })
      ).not.toBeInTheDocument();
      expect(saves).toHaveLength(0);
      expect(templates.get(1)).toEqual(original);
    }
  );

  it.each([
    '/admin/resource-templates',
    '/admin/resource-templates/new',
    '/admin/resource-templates/1',
  ])(
    'denies direct access to %s when the server rejects site admin access',
    async (initialPath) => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      mockAdminAccess();
      const list = vi.fn(() => HttpResponse.json([makeTemplate()]));
      const detail = vi.fn(() => HttpResponse.json(makeTemplate()));
      server.use(
        http.get(
          '/api/admin/access',
          () => new HttpResponse(null, { status: 403 })
        ),
        http.get('/api/admin/resource-templates', list),
        http.get('/api/admin/resource-templates/:id', detail)
      );
      ({ cleanup } = renderRoute({ initialPath }));

      expect(
        await screen.findByRole('heading', { name: 'Not authorized' })
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('textbox', { name: 'Template name' })
      ).not.toBeInTheDocument();
      expect(list).not.toHaveBeenCalled();
      expect(detail).not.toHaveBeenCalled();
    }
  );
});
