import { isDeepStrictEqual } from 'node:util';
import { createBrowserHistory } from '@tanstack/react-router';
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
    description: null,
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
    http.post('/api/admin/resource-templates/:id/duplicate', ({ params }) => {
      const previous = templates.get(Number(params.id))!;
      const id = Math.max(...templates.keys()) + 1;
      const copy = makeTemplate({
        description: previous.description,
        formJson: previous.formJson,
        id,
        name: `${previous.name} (copy)`,
      });
      templates.set(id, copy);
      return HttpResponse.json(copy, { status: 201 });
    }),
    http.put('/api/admin/resource-templates/:id', async ({ params, request }) => {
      const id = Number(params.id);
      const body = (await request.json()) as SaveResourceTemplateRequest;
      const previous = templates.get(id)!;
      if (
        !previous.isActive ||
        body.formSchemaVersion !== previous.formSchemaVersion ||
        body.updatedAt !== previous.updatedAt
      ) {
        return new HttpResponse(null, { status: 409 });
      }
      const updatedAt = new Date(
        Math.max(
          ...[...templates.values()].map((template) =>
            Date.parse(template.updatedAt)
          )
        ) + 1
      ).toISOString();
      const formChanged = !isDeepStrictEqual(
        JSON.parse(previous.formJson),
        JSON.parse(body.formJson)
      );
      const savedId = formChanged ? Math.max(...templates.keys()) + 1 : id;
      const template = makeTemplate({
        ...body,
        formJson: formChanged ? body.formJson : previous.formJson,
        formSchemaVersion: previous.formSchemaVersion + (formChanged ? 1 : 0),
        id: savedId,
        isActive: formChanged || body.isActive,
        updatedAt,
      });
      saves.push({
        body,
        id,
        token: request.headers.get('RequestVerificationToken'),
      });
      if (formChanged) {
        templates.set(id, { ...previous, isActive: false, updatedAt });
      }
      templates.set(savedId, template);
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
    expect(saves[0].body).not.toHaveProperty('updatedAt');
    expect(JSON.parse(templates.get(1)!.formJson).fields).toEqual([
      expect.objectContaining({
        label: 'Contact name',
        type: 'input',
        validation: { required: true },
      }),
    ]);
  });

  it('creates successive form versions while preserving earlier rows and reloading every field type', async () => {
    mockAdminAccess();
    const original = makeTemplate();
    const { saves, templates } = mockTemplateStore([original]);
    let rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;

    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Updated equipment booking' } }
    );
    fireEvent.click(screen.getByRole('button', { name: 'Move Visitor name down' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/2'
      )
    );
    expect(await screen.findByText('Version 2')).toBeInTheDocument();

    const expected = {
      fields: [
        definition.fields[1],
        definition.fields[0],
        ...definition.fields.slice(2),
      ],
    };
    expect(saves).toHaveLength(1);
    expect(JSON.parse(templates.get(2)!.formJson)).toEqual(expected);
    expect(templates.get(1)).toMatchObject({
      formJson: original.formJson,
      formSchemaVersion: 1,
      isActive: false,
      name: original.name,
    });
    const archivedOriginal = templates.get(1);
    expect(templates.get(2)).toMatchObject({ formSchemaVersion: 2, isActive: true });
    expect(saves[0].body).toMatchObject({
      formSchemaVersion: 1,
      updatedAt: original.updatedAt,
    });
    expect(saves[0].token).toBe('resource-template-test-token');
    cleanup?.();
    rendered = renderRoute({ initialPath: '/admin/resource-templates/2' });
    cleanup = rendered.cleanup;
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

    const versionTwoJson = templates.get(2)!.formJson;
    fireEvent.click(screen.getByRole('button', { name: 'Build form' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Label' }), {
      target: { value: 'Booking purpose' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/3'
      )
    );
    expect(await screen.findByText('Version 3')).toBeInTheDocument();
    expect(saves[1]).toMatchObject({ body: { formSchemaVersion: 2 }, id: 2 });
    expect(templates.get(1)).toEqual(archivedOriginal);
    expect(templates.get(2)).toMatchObject({
      formJson: versionTwoJson,
      formSchemaVersion: 2,
      isActive: false,
    });
    expect(templates.get(3)).toMatchObject({ formSchemaVersion: 3, isActive: true });
    expect(JSON.parse(templates.get(3)!.formJson).fields[0].label).toBe(
      'Booking purpose'
    );
  });

  it('duplicates a template and saves edits to the independent copy', async () => {
    mockAdminAccess();
    const original = makeTemplate({ formSchemaVersion: 4 });
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
    expect(
      screen.getByRole('columnheader', { name: 'Version' })
    ).toBeInTheDocument();
    fireEvent.click(within(row).getByRole('button', { name: /Duplicate/ }));
    expect(
      await screen.findByRole('textbox', { name: 'Template name' })
    ).toHaveValue('Equipment booking (copy)');
    expect(rendered.router.state.location.pathname).toBe(
      '/admin/resource-templates/2'
    );
    expect(screen.getByText('Version 1')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('textbox', { name: 'Template name' }), {
      target: { value: 'Camera booking' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Move Visitor name down' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/3'
      )
    );

    expect(duplicateToken).toBe('resource-template-test-token');
    expect(saves[0].id).toBe(2);
    expect(saves[0].body.formSchemaVersion).toBe(1);
    expect(templates.get(1)).toEqual(original);
    expect(templates.get(2)).toMatchObject({
      formJson: original.formJson,
      formSchemaVersion: 1,
      isActive: false,
    });
    expect(templates.get(3)!.formJson).not.toBe(original.formJson);
    expect(templates.get(3)!.formSchemaVersion).toBe(2);
  });

  it('makes a template read-only immediately after archiving it', async () => {
    mockAdminAccess();
    const { saves, templates } = mockTemplateStore();
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
    const active = await screen.findByRole('checkbox', { name: 'Active template' });

    fireEvent.click(active);
    expect(templates.get(1)!.isActive).toBe(true);
    expect(saves).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(
      await screen.findByRole('heading', { name: 'View resource template' })
    ).toBeInTheDocument();
    expect(templates.get(1)!.isActive).toBe(false);
    const archivedActive = screen.getByRole('checkbox', { name: 'Active template' });
    expect(archivedActive).toBeDisabled();
    expect(archivedActive).not.toBeChecked();
    expect(screen.getByRole('textbox', { name: 'Template name' })).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: 'Add Input' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Save template' })
    ).not.toBeInTheDocument();
    expect(
      screen.getByText(
        'Archived templates are read-only. Duplicate this template to make changes.'
      )
    ).toBeInTheDocument();
    expect(JSON.parse(templates.get(1)!.formJson)).toEqual(definition);
    expect(templates.size).toBe(1);
    expect(templates.get(1)!.formSchemaVersion).toBe(1);
    expect(saves).toHaveLength(1);
    expect(saves[0].body.updatedAt).toBe('2026-10-01T12:00:00Z');
  });

  it('opens archived forms read-only while allowing an interactive unsaved preview', async () => {
    mockAdminAccess();
    const original = makeTemplate({ formSchemaVersion: 3, isActive: false });
    const { saves, templates } = mockTemplateStore([original]);
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));

    expect(
      await screen.findByRole('heading', { name: 'View resource template' })
    ).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Template name' })).toBeDisabled();
    expect(
      screen.getByRole('checkbox', { name: 'Active template' })
    ).toBeDisabled();
    expect(
      screen.queryByRole('textbox', { name: 'Label' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Add Input' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Save template' })
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Back to resource templates' })
    ).toHaveAttribute('href', '/admin/resource-templates');
    expect(
      screen.queryByRole('button', { name: 'Build form' })
    ).not.toBeInTheDocument();
    const answer = screen.getByRole('textbox', { name: /Visitor name/ });
    expect(answer).toBeEnabled();
    fireEvent.change(answer, { target: { value: 'Taylor' } });
    expect(answer).toHaveValue('Taylor');
    expect(screen.getByRole('button', { name: 'Try validation' })).toBeEnabled();
    expect(saves).toHaveLength(0);
    expect(templates.get(1)).toEqual(original);
  });

  it('offers View and Duplicate for archived templates and opens the duplicate as active version one', async () => {
    mockAdminAccess();
    const archived = makeTemplate({ formSchemaVersion: 3, isActive: false });
    const { templates } = mockTemplateStore([archived]);
    const rendered = renderRoute({ initialPath: '/admin/resource-templates' });
    cleanup = rendered.cleanup;
    fireEvent.click(
      await screen.findByRole('checkbox', { name: 'Include archived templates' })
    );
    const row = within(
      await screen.findByRole('row', { name: /Equipment booking/ })
    );
    expect(
      row.getByRole('link', { name: 'View Equipment booking' })
    ).toHaveAttribute('href', '/admin/resource-templates/1');
    expect(row.queryByRole('link', { name: /Edit/ })).not.toBeInTheDocument();
    fireEvent.click(
      row.getByRole('button', { name: 'Duplicate Equipment booking' })
    );

    expect(
      await screen.findByRole('heading', { name: 'Edit resource template' })
    ).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe(
      '/admin/resource-templates/2'
    );
    expect(screen.getByText('Version 1')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Active template' })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Save template' })).toBeEnabled();
    expect(templates.get(1)).toEqual(archived);
    expect(templates.get(2)).toMatchObject({ formSchemaVersion: 1, isActive: true });
  });

  it('renames a later version without creating a row or rewriting equivalent form JSON', async () => {
    mockAdminAccess();
    const original = makeTemplate({
      formJson: JSON.stringify(definition, null, 2),
      formSchemaVersion: 3,
    });
    const { saves, templates } = mockTemplateStore([original]);
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;
    fireEvent.change(
      await screen.findByRole('textbox', { name: 'Template name' }),
      { target: { value: 'Renamed equipment booking' } }
    );
    expect(screen.getByText('Version 3')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

    expect(await screen.findByText(/Template saved/)).toBeInTheDocument();
    expect(rendered.router.state.location.pathname).toBe(
      '/admin/resource-templates/1'
    );
    expect(saves[0]).toMatchObject({ body: { formSchemaVersion: 3 }, id: 1 });
    expect(templates.size).toBe(1);
    expect(templates.get(1)).toEqual({
      ...original,
      name: 'Renamed equipment booking',
      updatedAt: expect.any(String),
    });
    expect(templates.get(1)!.updatedAt).not.toBe(original.updatedAt);
    const firstSaveTimestamp = templates.get(1)!.updatedAt;
    fireEvent.change(screen.getByRole('textbox', { name: 'Template name' }), {
      target: { value: 'Final equipment booking' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(await screen.findByText(/Template saved/)).toBeInTheDocument();
    expect(saves).toHaveLength(2);
    expect(saves[1].body.updatedAt).toBe(firstSaveTimestamp);
    expect(templates.get(1)).toMatchObject({
      formJson: original.formJson,
      formSchemaVersion: 3,
      name: 'Final equipment booking',
    });
    expect(templates.size).toBe(1);
  });

  it('keeps saving controls disabled until the refreshed template has finished loading', async () => {
    mockAdminAccess();
    const { saves, templates } = mockTemplateStore();
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
    const name = await screen.findByRole('textbox', { name: 'Template name' });
    const { promise: refreshStarted, resolve: markRefreshStarted } =
      Promise.withResolvers<void>();
    const { promise: refreshReady, resolve: releaseRefresh } =
      Promise.withResolvers<void>();
    server.use(
      http.get('/api/admin/resource-templates/1', async () => {
        markRefreshStarted();
        await refreshReady;
        return HttpResponse.json(templates.get(1));
      })
    );
    fireEvent.change(name, { target: { value: 'Renamed equipment booking' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

    try {
      await act(async () => {
        await refreshStarted;
      });
      expect(templates.get(1)!.name).toBe('Renamed equipment booking');
      expect(saves).toHaveLength(1);
      expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
      expect(name).toBeDisabled();
      expect(
        screen.getByRole('checkbox', { name: 'Active template' })
      ).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Add Input' })).toBeDisabled();
      expect(screen.getByRole('textbox', { name: 'Label' })).toBeDisabled();
      fireEvent.submit(name.closest('form')!);
    } finally {
      await act(async () => {
        releaseRefresh();
      });
    }

    expect(await screen.findByText(/Template saved/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save template' })).toBeEnabled();
    expect(name).toBeEnabled();
    expect(saves).toHaveLength(1);
  });

  it.each(['changed', 'archived'])(
    'retains the draft when another administrator has already %s the saved template',
    async (action) => {
      mockAdminAccess();
      const original = makeTemplate();
      const { saves, templates } = mockTemplateStore([original]);
      ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
      fireEvent.change(
        await screen.findByRole('textbox', { name: 'Template name' }),
        { target: { value: 'My unfinished version' } }
      );
      fireEvent.change(screen.getByRole('textbox', { name: 'Label' }), {
        target: { value: 'Contact name' },
      });
      const externalUpdate = {
        ...original,
        isActive: action !== 'archived',
        name:
          action === 'changed'
            ? 'Another administrator changed this'
            : original.name,
        updatedAt:
          action === 'changed' ? '2026-10-01T12:01:00Z' : original.updatedAt,
      };
      templates.set(1, externalUpdate);
      fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(
        /changed|archived/i
      );
      expect(screen.getByRole('textbox', { name: 'Template name' })).toHaveValue(
        'My unfinished version'
      );
      expect(screen.getByRole('textbox', { name: 'Label' })).toHaveValue(
        'Contact name'
      );
      expect(screen.getByRole('button', { name: 'Save template' })).toBeEnabled();
      expect(saves).toHaveLength(0);
      expect(templates.size).toBe(1);
      expect(templates.get(1)).toEqual(externalUpdate);
    }
  );

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
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;

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

    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/2'
      )
    );
    expect(saves[0].body.name).toBe('My unsaved changes');
    expect(JSON.parse(saves[0].body.formJson).fields[0].id).toBe('purpose');
  });

  it('blocks invalid length rules before sending changes to the server', async () => {
    mockAdminAccess();
    const { saves } = mockTemplateStore();
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;

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

    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/2'
      )
    );
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

  it('preserves an open draft when the query cache receives an unsupported form definition', async () => {
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
        makeTemplate({
          formJson: '{"fields":[],"futureFeature":true}',
          name: 'External update',
        })
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
    const previousUrl = window.location.href;
    const previousState = window.history.state;
    window.history.replaceState(null, '', '/admin/resource-templates/1');
    const history = createBrowserHistory();
    const rendered = renderRoute({ history });
    cleanup = () => {
      rendered.cleanup();
      history.destroy();
      window.history.replaceState(previousState, '', previousUrl);
    };
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
    { formJson: '{"fields":[]}', formSchemaVersion: 0 },
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
        /not supported|cannot be edited safely|invalid revision/i
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
