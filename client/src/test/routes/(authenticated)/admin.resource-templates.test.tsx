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

const resourceDefaults = {
  billingRates: [
    {
      amount: '25.00',
      basis: 'hour',
      currency: 'USD',
      id: 'fb7a388a-ef81-4cc0-aec2-f8772796eb42',
      name: 'Standard',
    },
  ],
  openingHours: {
    monday: [{ end: '17:00', start: '09:00' }],
    sunday: [],
  },
  schemaVersion: 1,
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
    resourceDefaultsJson: null,
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
    http.get('/api/antiforgery', () =>
      HttpResponse.json({
        formFieldName: '__RequestVerificationToken',
        requestToken: 'resource-template-test-token',
      })
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
        resourceDefaultsJson: previous.resourceDefaultsJson,
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
      const resourceDefaultsJson =
        body.resourceDefaultsJson === undefined
          ? previous.resourceDefaultsJson
          : body.resourceDefaultsJson?.trim()
            ? body.resourceDefaultsJson
            : null;
      const revisionChanged =
        formChanged || resourceDefaultsJson !== previous.resourceDefaultsJson;
      const savedId = revisionChanged ? Math.max(...templates.keys()) + 1 : id;
      const template = makeTemplate({
        ...body,
        formJson: formChanged ? body.formJson : previous.formJson,
        formSchemaVersion: previous.formSchemaVersion + (revisionChanged ? 1 : 0),
        id: savedId,
        isActive: revisionChanged || body.isActive,
        resourceDefaultsJson,
        updatedAt,
      });
      saves.push({
        body,
        id,
        token: request.headers.get('RequestVerificationToken'),
      });
      if (revisionChanged) {
        templates.set(id, { ...previous, isActive: false, updatedAt });
      }
      templates.set(savedId, template);
      return HttpResponse.json(template);
    })
  );
  return { saves, templates };
}

describe('site admin resource templates', () => {
  it.each([
    {
      action: 'Create template',
      fieldType: 'Input',
      initialPath: '/admin/resource-templates/new',
      type: 'input',
    },
    {
      action: 'Create template',
      fieldType: 'Text block',
      initialPath: '/admin/resource-templates/new',
      type: 'text',
    },
    {
      action: 'Save template',
      fieldType: 'Input',
      initialPath: '/admin/resource-templates/1',
      type: 'input',
    },
    {
      action: 'Save template',
      fieldType: 'Text block',
      initialPath: '/admin/resource-templates/1',
      type: 'text',
    },
  ])(
    'requires a form item before $action and accepts $fieldType',
    async ({ action, fieldType, initialPath, type }) => {
      mockAdminAccess();
      const original = makeTemplate({
        formJson: JSON.stringify({
          fields: [{ id: 'name', label: 'Visitor name', type: 'input' }],
        }),
      });
      const { saves, templates } = mockTemplateStore([original]);
      ({ cleanup } = renderRoute({ initialPath }));
      fireEvent.change(
        await screen.findByRole('textbox', { name: 'Template name' }),
        { target: { value: 'Camera checkout' } }
      );
      if (action === 'Save template') {
        fireEvent.click(
          screen.getByRole('button', { name: 'Remove Visitor name' })
        );
      }

      fireEvent.click(screen.getByRole('button', { name: action }));
      expect(
        await screen.findByText(
          'Add at least one form field before saving the template.'
        )
      ).toBeInTheDocument();
      expect(saves).toHaveLength(0);
      expect(templates.get(1)).toEqual(original);

      fireEvent.click(screen.getByRole('button', { name: `Add ${fieldType}` }));
      fireEvent.click(screen.getByRole('button', { name: action }));
      await waitFor(() => expect(saves).toHaveLength(1));
      expect(JSON.parse(saves[0].body.formJson).fields).toEqual([
        expect.objectContaining({ type }),
      ]);
    }
  );

  it('lets an existing empty template be opened and repaired before saving', async () => {
    mockAdminAccess();
    const { saves } = mockTemplateStore([
      makeTemplate({ formJson: '{"fields":[]}' }),
    ]);
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));

    fireEvent.click(await screen.findByRole('button', { name: 'Add Input' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() => expect(saves).toHaveLength(1));
    expect(JSON.parse(saves[0].body.formJson).fields.at(-1).type).toBe('input');
  });

  it('associates template-name errors with the input and clears them after correction', async () => {
    mockAdminAccess();
    const { saves } = mockTemplateStore();
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
    const name = await screen.findByRole('textbox', { name: 'Template name' });
    expect(name).not.toHaveAttribute('aria-invalid');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();

    fireEvent.change(name, { target: { value: '   ' } });
    fireEvent.blur(name);
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() => expect(name).toHaveAttribute('aria-invalid', 'true'));
    expect(name).toHaveAccessibleDescription(
      'Enter a template name of 200 characters or fewer.'
    );
    expect(saves).toHaveLength(0);

    fireEvent.change(name, { target: { value: 'Camera checkout' } });
    await waitFor(() => expect(name).not.toHaveAttribute('aria-invalid'));
    expect(name).not.toHaveAttribute('aria-describedby');
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(await screen.findByText('Template saved.')).toBeInTheDocument();
    expect(saves).toHaveLength(1);
    expect(saves[0].body.name).toBe('Camera checkout');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

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
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));
    fireEvent.click(
      screen.getByRole('checkbox', { name: 'Include default opening hours' })
    );
    fireEvent.change(screen.getByRole('combobox', { name: 'Monday hours' }), {
      target: { value: 'open' },
    });
    fireEvent.change(screen.getByLabelText('Monday opening time 1'), {
      target: { value: '09:00' },
    });
    fireEvent.change(screen.getByLabelText('Monday closing time 1'), {
      target: { value: '17:00' },
    });
    fireEvent.change(screen.getByRole('combobox', { name: 'Sunday hours' }), {
      target: { value: 'closed' },
    });
    fireEvent.click(
      screen.getByRole('checkbox', { name: 'Include default billing rates' })
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Rate name 1' }), {
      target: { value: 'Standard' },
    });
    fireEvent.change(screen.getByRole('textbox', { name: 'Amount (USD) 1' }), {
      target: { value: '25.00' },
    });
    expect(screen.queryByRole('textbox', { name: 'Currency 1' })).not.toBeInTheDocument();
    fireEvent.change(screen.getByRole('combobox', { name: 'Rate basis 1' }), {
      target: { value: 'hour' },
    });
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
      body: {
        formSchemaVersion: 1,
        isActive: true,
        name: 'Camera checkout',
      },
      id: 1,
      token: 'resource-template-test-token',
    });
    expect(saves[0].body).not.toHaveProperty('updatedAt');
    expect(JSON.parse(saves[0].body.resourceDefaultsJson!)).toEqual({
      ...resourceDefaults,
      billingRates: [
        {
          amount: '25.00',
          basis: 'hour',
          id: expect.any(String),
          name: 'Standard',
        },
      ],
    });
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));
    expect(screen.getByLabelText('Monday opening time 1')).toHaveValue('09:00');
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toHaveValue(
      '25.00'
    );
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
    const original = makeTemplate({
      formSchemaVersion: 4,
      resourceDefaultsJson: JSON.stringify(resourceDefaults),
    });
    const { saves, templates } = mockTemplateStore([original]);
    let duplicateToken: string | null = null;
    server.use(
      http.post('/api/admin/resource-templates/1/duplicate', ({ request }) => {
        duplicateToken = request.headers.get('RequestVerificationToken');
        const copy = makeTemplate({
          id: 2,
          name: 'Equipment booking (copy)',
          resourceDefaultsJson: original.resourceDefaultsJson,
        });
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
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toHaveValue(
      '25.00'
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Amount (USD) 1' }), {
      target: { value: '50.00' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Build form' }));
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
      resourceDefaultsJson: original.resourceDefaultsJson,
    });
    expect(templates.get(3)!.formJson).not.toBe(original.formJson);
    expect(templates.get(3)!.formSchemaVersion).toBe(2);
    expect(JSON.parse(templates.get(3)!.resourceDefaultsJson!)).toEqual({
      ...resourceDefaults,
      billingRates: [{ ...resourceDefaults.billingRates[0], amount: '50.00' }],
    });
  });

  it('makes a template read-only immediately after archiving it', async () => {
    mockAdminAccess();
    const { saves, templates } = mockTemplateStore([
      makeTemplate({ resourceDefaultsJson: JSON.stringify(resourceDefaults) }),
    ]);
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
    const active = await screen.findByRole('checkbox', { name: 'Active template' });
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));

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
      screen.getByRole('button', { name: 'Resource defaults', pressed: true })
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Monday opening time 1')).toHaveValue('09:00');
    expect(screen.getByLabelText('Monday opening time 1')).toBeDisabled();
    expect(screen.getByLabelText('Monday closing time 1')).toHaveValue('17:00');
    expect(screen.getByLabelText('Monday closing time 1')).toBeDisabled();
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toHaveValue('25.00');
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toBeDisabled();
    for (const name of [
      'Add Monday hours',
      'Remove Monday hours 1',
      'Add billing rate',
      'Remove billing rate 1',
    ]) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument();
    }
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
    expect(JSON.parse(templates.get(1)!.resourceDefaultsJson!)).toEqual(resourceDefaults);
    expect(templates.size).toBe(1);
    expect(templates.get(1)!.formSchemaVersion).toBe(1);
    expect(saves).toHaveLength(1);
    expect(saves[0].body.updatedAt).toBe('2026-10-01T12:00:00Z');
  });

  it('opens archived forms read-only while allowing an interactive unsaved preview', async () => {
    mockAdminAccess();
    const original = makeTemplate({
      formSchemaVersion: 3,
      isActive: false,
      resourceDefaultsJson: JSON.stringify(resourceDefaults),
    });
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
    expect(
      screen.getByRole('button', { name: 'Try validation' })
    ).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));
    expect(screen.getByLabelText('Monday opening time 1')).toHaveValue('09:00');
    expect(screen.getByLabelText('Monday opening time 1')).toBeDisabled();
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toHaveValue(
      '25.00'
    );
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toBeDisabled();
    expect(
      screen.getByRole('checkbox', { name: 'Include default opening hours' })
    ).toBeDisabled();
    expect(
      screen.getByRole('checkbox', { name: 'Include default billing rates' })
    ).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Preview form' }));
    expect(screen.getByRole('textbox', { name: /Visitor name/ })).toBeEnabled();
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
      resourceDefaultsJson: JSON.stringify(resourceDefaults, null, 2),
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

  it('creates successive versions when editing and clearing resource defaults', async () => {
    mockAdminAccess();
    const original = makeTemplate({
      formJson: JSON.stringify(definition, null, 2),
      formSchemaVersion: 3,
      resourceDefaultsJson: JSON.stringify(resourceDefaults),
    });
    const changedDefaults = {
      ...resourceDefaults,
      billingRates: [{ ...resourceDefaults.billingRates[0], amount: '40.00' }],
      openingHours: {
        ...resourceDefaults.openingHours,
        monday: [{ end: '18:00', start: '10:00' }],
      },
    };
    const { saves, templates } = mockTemplateStore([original]);
    let rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;

    fireEvent.click(
      await screen.findByRole('button', { name: 'Resource defaults' })
    );
    fireEvent.change(screen.getByLabelText('Monday opening time 1'), {
      target: { value: '10:00' },
    });
    fireEvent.change(screen.getByLabelText('Monday closing time 1'), {
      target: { value: '18:00' },
    });
    expect(
      screen.queryByRole('checkbox', { name: /closes next day/i })
    ).not.toBeInTheDocument();
    fireEvent.change(screen.getByRole('textbox', { name: 'Amount (USD) 1' }), {
      target: { value: '40.00' },
    });
    expect(screen.getByText('You have unsaved changes.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Preview form' }));
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));
    expect(screen.getByLabelText('Monday opening time 1')).toHaveValue('10:00');
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toHaveValue(
      '40.00'
    );
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/2'
      )
    );
    expect(await screen.findByText('Version 4')).toBeInTheDocument();
    expect(templates.size).toBe(2);
    expect(templates.get(1)).toEqual({
      ...original,
      isActive: false,
      updatedAt: expect.any(String),
    });
    const archivedOriginal = templates.get(1);
    expect(templates.get(2)).toMatchObject({
      formJson: original.formJson,
      formSchemaVersion: 4,
      isActive: true,
    });
    expect(saves[0]).toMatchObject({ body: { formSchemaVersion: 3 }, id: 1 });
    expect(JSON.parse(saves[0].body.resourceDefaultsJson!)).toEqual(
      changedDefaults
    );
    const changedDefaultsJson = templates.get(2)!.resourceDefaultsJson;
    expect(JSON.parse(changedDefaultsJson!)).toEqual(changedDefaults);

    cleanup?.();
    rendered = renderRoute({ initialPath: '/admin/resource-templates/2' });
    cleanup = rendered.cleanup;
    fireEvent.click(
      await screen.findByRole('button', { name: 'Resource defaults' })
    );
    expect(screen.getByLabelText('Monday opening time 1')).toHaveValue('10:00');
    expect(screen.getByLabelText('Monday closing time 1')).toHaveValue('18:00');
    expect(screen.getByRole('textbox', { name: 'Amount (USD) 1' })).toHaveValue(
      '40.00'
    );
    fireEvent.click(
      screen.getByRole('checkbox', { name: 'Include default opening hours' })
    );
    fireEvent.click(
      screen.getByRole('checkbox', { name: 'Include default billing rates' })
    );
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/3'
      )
    );
    expect(await screen.findByText('Version 5')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));
    expect(
      screen.getByRole('checkbox', { name: 'Include default opening hours' })
    ).not.toBeChecked();
    expect(
      screen.getByRole('checkbox', { name: 'Include default billing rates' })
    ).not.toBeChecked();
    expect(saves[1].body.resourceDefaultsJson).toBeNull();
    expect(saves[1]).toMatchObject({ body: { formSchemaVersion: 4 }, id: 2 });
    expect(templates.size).toBe(3);
    expect(templates.get(1)).toEqual(archivedOriginal);
    expect(templates.get(2)).toMatchObject({
      formJson: original.formJson,
      formSchemaVersion: 4,
      isActive: false,
      resourceDefaultsJson: changedDefaultsJson,
    });
    expect(templates.get(3)).toMatchObject({
      formJson: original.formJson,
      formSchemaVersion: 5,
      isActive: true,
      resourceDefaultsJson: null,
    });
  });

  it('keeps an originally configured day unspecified across view changes and saving', async () => {
    mockAdminAccess();
    const { saves } = mockTemplateStore([
      makeTemplate({ resourceDefaultsJson: JSON.stringify(resourceDefaults) }),
    ]);
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;
    fireEvent.click(
      await screen.findByRole('button', { name: 'Resource defaults' })
    );
    fireEvent.change(screen.getByRole('combobox', { name: 'Monday hours' }), {
      target: { value: 'unspecified' },
    });
    expect(
      screen.queryByLabelText('Monday opening time 1')
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Preview form' }));
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));
    expect(screen.getByRole('combobox', { name: 'Monday hours' })).toHaveValue(
      'unspecified'
    );
    expect(
      screen.queryByLabelText('Monday opening time 1')
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() => {
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/2'
      );
      expect(screen.getByText('Version 2')).toBeInTheDocument();
    });
    expect(saves).toHaveLength(1);
    expect(JSON.parse(saves[0].body.resourceDefaultsJson!)).toEqual({
      ...resourceDefaults,
      openingHours: { sunday: [] },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Resource defaults' }));
    expect(screen.getByRole('combobox', { name: 'Monday hours' })).toHaveValue(
      'unspecified'
    );
    expect(
      screen.queryByLabelText('Monday opening time 1')
    ).not.toBeInTheDocument();
  });

  it.each(['reversed', 'overlapping'])(
    'blocks %s opening hours from another view and allows correction',
    async (invalidHours) => {
      mockAdminAccess();
      const { saves } = mockTemplateStore([
        makeTemplate({
          resourceDefaultsJson: JSON.stringify(resourceDefaults),
        }),
      ]);
      ({ cleanup } = renderRoute({
        initialPath: '/admin/resource-templates/1',
      }));
      fireEvent.click(
        await screen.findByRole('button', { name: 'Resource defaults' })
      );
      if (invalidHours === 'reversed') {
        fireEvent.change(screen.getByLabelText('Monday closing time 1'), {
          target: { value: '08:00' },
        });
      } else {
        fireEvent.click(
          screen.getByRole('button', { name: 'Add Monday hours' })
        );
      }
      fireEvent.click(screen.getByRole('button', { name: 'Preview form' }));
      expect(
        screen.queryByLabelText('Monday closing time 1')
      ).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

      expect(
        await screen.findByRole('button', {
          name: 'Resource defaults',
          pressed: true,
        })
      ).toBeInTheDocument();
      const closingTime = screen.getByLabelText('Monday closing time 1');
      expect(closingTime).toBeEnabled();
      await waitFor(() =>
        expect(closingTime).toHaveAttribute('aria-invalid', 'true')
      );
      expect(closingTime).toHaveAccessibleDescription(
        invalidHours === 'reversed'
          ? /closing time must be after/
          : /must not overlap/
      );
      if (invalidHours === 'overlapping') {
        expect(
          screen.getByLabelText('Monday opening time 2')
        ).toHaveAccessibleDescription(/must not overlap/);
      }
      expect(screen.getAllByRole('alert')[0]).toHaveTextContent(
        /monday|overlap/i
      );
      expect(saves).toHaveLength(0);
      if (invalidHours === 'reversed') {
        fireEvent.change(screen.getByLabelText('Monday closing time 1'), {
          target: { value: '17:00' },
        });
      } else {
        fireEvent.click(
          screen.getByRole('button', { name: 'Remove Monday hours 2' })
        );
      }
      fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
      expect(await screen.findByText('Template saved.')).toBeInTheDocument();
      expect(saves).toHaveLength(1);
      expect(
        screen.getByLabelText('Monday closing time 1')
      ).not.toHaveAttribute('aria-invalid');
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(JSON.parse(saves[0].body.resourceDefaultsJson!)).toEqual(
        resourceDefaults
      );
    }
  );

  it.each([
    {
      field: 'Amount (USD) 1',
      kind: 'negative amount',
      repair: '25.00',
      value: '-1',
    },
    { field: 'Amount (USD) 1', kind: 'missing amount', repair: '25.00', value: '' },
    {
      field: 'Rate name 1',
      kind: 'missing name',
      repair: 'Standard',
      value: '',
    },
  ])(
    'blocks a billing rate with $kind from another view and allows correction',
    async ({ field, repair, value }) => {
      mockAdminAccess();
      const { saves } = mockTemplateStore([
        makeTemplate({
          resourceDefaultsJson: JSON.stringify(resourceDefaults),
        }),
      ]);
      ({ cleanup } = renderRoute({
        initialPath: '/admin/resource-templates/1',
      }));
      fireEvent.click(
        await screen.findByRole('button', { name: 'Resource defaults' })
      );
      fireEvent.change(screen.getByRole('textbox', { name: field }), {
        target: { value },
      });
      fireEvent.click(screen.getByRole('button', { name: 'Build form' }));
      expect(
        screen.queryByRole('textbox', { name: field })
      ).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

      expect(
        await screen.findByRole('button', {
          name: 'Resource defaults',
          pressed: true,
        })
      ).toBeInTheDocument();
      expect(screen.getAllByRole('alert')[0]).toHaveTextContent(/rate/i);
      const input = screen.getByRole('textbox', { name: field });
      expect(input).toBeEnabled();
      await waitFor(() =>
        expect(input).toHaveAttribute('aria-invalid', 'true')
      );
      expect(input).toHaveAccessibleDescription(/Billing rate 1: enter/);
      expect(saves).toHaveLength(0);
      fireEvent.change(input, { target: { value: repair } });
      fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
      expect(await screen.findByText('Template saved.')).toBeInTheDocument();
      expect(saves).toHaveLength(1);
      expect(screen.getByRole('textbox', { name: field })).not.toHaveAttribute(
        'aria-invalid'
      );
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(JSON.parse(saves[0].body.resourceDefaultsJson!)).toEqual(
        resourceDefaults
      );
    }
  );

  it('keeps errors with the remaining rate when an earlier invalid rate is removed', async () => {
    mockAdminAccess();
    const retainedRate = {
      ...resourceDefaults.billingRates[0],
      id: 'retained-rate',
      name: 'Retained rate',
    };
    const { saves } = mockTemplateStore([
      makeTemplate({
        resourceDefaultsJson: JSON.stringify({
          ...resourceDefaults,
          billingRates: [resourceDefaults.billingRates[0], retainedRate],
        }),
      }),
    ]);
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
    fireEvent.click(
      await screen.findByRole('button', { name: 'Resource defaults' })
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Rate name 1' }), {
      target: { value: '' },
    });
    fireEvent.change(screen.getByRole('textbox', { name: 'Amount (USD) 2' }), {
      target: { value: '-1' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() =>
      expect(
        screen.getByRole('textbox', { name: 'Amount (USD) 2' })
      ).toHaveAccessibleDescription(
        /Billing rate 2: enter a nonnegative amount/
      )
    );
    expect(
      screen.getByRole('textbox', { name: 'Rate name 1' })
    ).toHaveAccessibleDescription(/enter a name/);
    expect(saves).toHaveLength(0);

    fireEvent.click(
      screen.getByRole('button', { name: 'Remove billing rate 1' })
    );
    const amount = screen.getByRole('textbox', { name: 'Amount (USD) 1' });
    await waitFor(() =>
      expect(amount).toHaveAccessibleDescription(
        /Billing rate 1: enter a nonnegative amount/
      )
    );
    expect(amount).toHaveValue('-1');
    expect(screen.getByRole('textbox', { name: 'Rate name 1' })).toHaveValue(
      'Retained rate'
    );
    expect(
      screen.getByRole('textbox', { name: 'Rate name 1' })
    ).not.toHaveAttribute('aria-invalid');
    expect(
      screen.queryByRole('textbox', { name: 'Amount (USD) 2' })
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    expect(saves).toHaveLength(0);

    fireEvent.change(amount, { target: { value: '30.00' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() => expect(saves).toHaveLength(1));
    expect(JSON.parse(saves[0].body.resourceDefaultsJson!)).toEqual({
      ...resourceDefaults,
      billingRates: [{ ...retainedRate, amount: '30.00' }],
    });
  });

  it('clears errors from disabled defaults sections and allows saving their removal', async () => {
    mockAdminAccess();
    const { saves } = mockTemplateStore([
      makeTemplate({ resourceDefaultsJson: JSON.stringify(resourceDefaults) }),
    ]);
    ({ cleanup } = renderRoute({ initialPath: '/admin/resource-templates/1' }));
    fireEvent.click(
      await screen.findByRole('button', { name: 'Resource defaults' })
    );
    fireEvent.change(screen.getByLabelText('Monday closing time 1'), {
      target: { value: '08:00' },
    });
    fireEvent.change(screen.getByRole('textbox', { name: 'Amount (USD) 1' }), {
      target: { value: '-1' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() =>
      expect(screen.getByLabelText('Monday closing time 1')).toHaveAttribute(
        'aria-invalid',
        'true'
      )
    );
    expect(
      screen.getByRole('textbox', { name: 'Amount (USD) 1' })
    ).toHaveAttribute('aria-invalid', 'true');
    expect(saves).toHaveLength(0);

    fireEvent.click(
      screen.getByRole('checkbox', { name: 'Include default opening hours' })
    );
    fireEvent.click(
      screen.getByRole('checkbox', { name: 'Include default billing rates' })
    );
    await waitFor(() =>
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    );
    expect(
      screen.queryByLabelText('Monday closing time 1')
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('textbox', { name: 'Amount (USD) 1' })
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));
    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].body.resourceDefaultsJson).toBeNull();
  });

  it.each([
    {
      kind: 'array',
      resourceDefaultsJson: '[\n  { "futureSetting": true }\n]',
    },
    {
      kind: 'future schema',
      resourceDefaultsJson: '{ "schemaVersion": 2, "openingHours": { "monday": [] } }',
    },
    {
      kind: 'unrecognized billing rates',
      resourceDefaultsJson: '{ "schemaVersion": 1, "billingRates": { "hourly": 25 } }',
    },
    {
      kind: 'oversized defaults',
      resourceDefaultsJson: JSON.stringify({ padding: 'x'.repeat(1024 * 1024) }),
    },
  ])(
    'preserves unsupported saved defaults while saving metadata ($kind)',
    async ({ resourceDefaultsJson }) => {
      mockAdminAccess();
      const { saves, templates } = mockTemplateStore([
        makeTemplate({ resourceDefaultsJson }),
      ]);
      ({ cleanup } = renderRoute({
        initialPath: '/admin/resource-templates/1',
      }));
      fireEvent.click(
        await screen.findByRole('button', { name: 'Resource defaults' })
      );
      expect(
        screen.getByText(
          'These saved defaults use a format this editor cannot change. They will be preserved when you save other template changes.'
        )
      ).toBeInTheDocument();
      expect(
        screen.queryByRole('checkbox', {
          name: 'Include default billing rates',
        })
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('textbox', { name: 'Resource defaults JSON' })
      ).not.toBeInTheDocument();
      fireEvent.change(screen.getByRole('textbox', { name: 'Template name' }), {
        target: { value: 'Renamed without changing defaults' },
      });
      fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

      expect(await screen.findByText('Template saved.')).toBeInTheDocument();
      expect(saves[0].body).not.toHaveProperty('resourceDefaultsJson');
      expect(templates.get(1)!.resourceDefaultsJson).toBe(resourceDefaultsJson);
    }
  );

  it('preserves unsupported defaults when saving a new form version', async () => {
    mockAdminAccess();
    const resourceDefaultsJson = '{ "schemaVersion": 2, "futureSetting": true }';
    const { saves, templates } = mockTemplateStore([
      makeTemplate({ resourceDefaultsJson }),
    ]);
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;
    fireEvent.change(await screen.findByRole('textbox', { name: 'Label' }), {
      target: { value: 'Contact name' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

    await waitFor(() =>
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/2'
      )
    );
    expect(saves[0].body).not.toHaveProperty('resourceDefaultsJson');
    expect(templates.get(1)).toMatchObject({ isActive: false, resourceDefaultsJson });
    expect(templates.get(2)).toMatchObject({
      formSchemaVersion: 2,
      isActive: true,
      resourceDefaultsJson,
    });
  });

  it('preserves unknown root and nested properties when editing known defaults', async () => {
    mockAdminAccess();
    const originalDefaults = {
      ...resourceDefaults,
      billingRates: [
        { ...resourceDefaults.billingRates[0], futureCategory: 'staff' },
      ],
      futureSetting: { enabled: true },
      openingHours: {
        ...resourceDefaults.openingHours,
        monday: [
          { ...resourceDefaults.openingHours.monday[0], futureNote: 'staffed' },
        ],
        timeZone: 'America/Los_Angeles',
      },
    };
    const { saves, templates } = mockTemplateStore([
      makeTemplate({ resourceDefaultsJson: JSON.stringify(originalDefaults) }),
    ]);
    const rendered = renderRoute({ initialPath: '/admin/resource-templates/1' });
    cleanup = rendered.cleanup;
    fireEvent.click(
      await screen.findByRole('button', { name: 'Resource defaults' })
    );
    fireEvent.change(screen.getByLabelText('Monday opening time 1'), {
      target: { value: '10:00' },
    });
    fireEvent.change(screen.getByRole('textbox', { name: 'Amount (USD) 1' }), {
      target: { value: '30.00' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save template' }));

    await waitFor(() => {
      expect(rendered.router.state.location.pathname).toBe(
        '/admin/resource-templates/2'
      );
      expect(screen.getByText('Version 2')).toBeInTheDocument();
    });
    expect(templates.get(1)).toMatchObject({
      formSchemaVersion: 1,
      isActive: false,
      resourceDefaultsJson: JSON.stringify(originalDefaults),
    });
    expect(templates.get(2)).toMatchObject({
      formSchemaVersion: 2,
      isActive: true,
    });
    expect(JSON.parse(saves[0].body.resourceDefaultsJson!)).toEqual({
      ...originalDefaults,
      billingRates: [{ ...originalDefaults.billingRates[0], amount: '30.00' }],
      openingHours: {
        ...originalDefaults.openingHours,
        monday: [
          { ...originalDefaults.openingHours.monday[0], start: '10:00' },
        ],
      },
    });
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
