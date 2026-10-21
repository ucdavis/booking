import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useBlocker } from '@tanstack/react-router';
import { useMemo, useRef, useState } from 'react';
import type { ResourceDetail } from './models/ResourceDetail.ts';
import type { ResourceOptions } from './models/ResourceOptions.ts';
import type { SaveResourceRequest } from './models/SaveResourceRequest.ts';
import { ResourceDefaultsEditor } from '@/features/resource-defaults/ResourceDefaultsEditor.tsx';
import {
  parseResourceDefaultsJson,
  resourceDefaultsFormValues,
  resourceDefaultsFromForm,
  serializeResourceDefaults,
  validateResourceDefaults,
  validateResourceDefaultsFields,
} from '@/features/resource-defaults/resourceDefaults.ts';
import { FormBuilder } from '@/features/resource-templates/FormBuilder.tsx';
import { FormPreview } from '@/features/resource-templates/FormPreview.tsx';
import {
  parseFormDefinition,
  validateFormDefinition,
} from '@/features/resource-templates/formDefinition.ts';
import type { FormDefinition } from '@/features/resource-templates/models/FormDefinition.ts';
import type { ResourceTemplate } from '@/features/resource-templates/models/ResourceTemplate.ts';
import {
  createResource,
  resourceErrorMessage,
  resourceQueryOptions,
  resourcesKey,
  updateResource,
} from '@/queries/resources.ts';
import { useAppForm } from '@/shared/forms/formContext.tsx';

const standardCategories = ['Room', 'Bench', 'Equipment'];

function readConfiguration(
  resource?: ResourceDetail,
  template?: ResourceTemplate
) {
  const formJson = resource?.config?.formJson ?? template?.formJson;
  const formSchemaVersion =
    resource?.config?.formSchemaVersion ?? template?.formSchemaVersion ?? 1;
  const detailsJson =
    resource?.config?.detailsJson ?? template?.resourceDefaultsJson;
  const detailsSchemaVersion = resource?.config?.detailsSchemaVersion ?? 1;
  const defaults = parseResourceDefaultsJson(detailsJson, 'resource');
  let definition: FormDefinition = { fields: [] };
  let formUnavailableReason: string | undefined;
  if (formJson) {
    try {
      definition = parseFormDefinition(formJson, formSchemaVersion);
    } catch (error) {
      formUnavailableReason =
        error instanceof Error
          ? error.message
          : 'This form uses a format the editor does not support.';
    }
  } else if (resource) {
    formUnavailableReason =
      'This resource does not have a saved configuration.';
  }
  const detailsUnavailableReason =
    resource && !resource.config
      ? 'This resource does not have a saved configuration. Other resource changes can still be saved.'
      : detailsSchemaVersion !== 1
        ? 'These settings use a newer format. They will be preserved when you save other resource changes.'
        : defaults.unavailableReason;
  return {
    defaults: defaults.defaults,
    definition,
    detailsJson,
    detailsSchemaVersion,
    detailsUnavailableReason,
    formJson,
    formSchemaVersion,
    formUnavailableReason,
  };
}

function resourceFormValues(
  resource: ResourceDetail | undefined,
  configuration: ReturnType<typeof readConfiguration>,
  options: ResourceOptions
) {
  const savedCategory = resource?.category ?? '';
  const standardCategory = standardCategories.find(
    (category) => category.toLowerCase() === savedCategory.trim().toLowerCase()
  );
  return {
    allowReservationSharing: resource?.allowReservationSharing ?? false,
    approvalMode: resource?.approvalMode ?? 'manual',
    capacity: resource?.capacity == null ? '' : String(resource.capacity),
    category: standardCategory ?? (savedCategory ? 'Other' : ''),
    customCategory: standardCategory ? '' : savedCategory,
    definition: configuration.definition,
    description: resource?.description ?? '',
    followTeamCalendar: resource?.followTeamCalendar ?? true,
    isActive: resource?.isActive ?? true,
    isReservable: resource?.isReservable ?? true,
    name: resource?.name ?? '',
    newSpace: {
      address: '',
      name: '',
    },
    publicAccess: resource?.publicAccess ?? 'availability',
    resourceDefaults: resourceDefaultsFormValues(configuration.defaults),
    sourceTemplateId: '',
    spaceId: resource
      ? String(resource.spaceId)
      : options.spaces.length
        ? ''
        : 'new',
  };
}

export function ResourceEditor({
  onSaved,
  options,
  resource,
  teamSlug,
}: {
  onSaved: (saved: ResourceDetail) => void | Promise<void>;
  options: ResourceOptions;
  resource?: ResourceDetail;
  teamSlug: string;
}) {
  const queryClient = useQueryClient();
  // Keep the draft and its concurrency tokens together when queries refetch.
  const [baseline, setBaseline] = useState(resource);
  const [selectedTemplate, setSelectedTemplate] = useState<ResourceTemplate>();
  const configuration = useMemo(
    () => readConfiguration(baseline, selectedTemplate),
    [baseline, selectedTemplate]
  );
  const [editorView, setEditorView] = useState<
    'build' | 'preview' | 'defaults'
  >('build');
  const [saved, setSaved] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const saveFlowRef = useRef(false);
  const activeTemplates = options.templates.filter(
    (template) => template.isActive
  );
  const saveMutation = useMutation({
    mutationFn: (request: SaveResourceRequest) =>
      baseline
        ? updateResource(teamSlug, baseline.id, request)
        : createResource(teamSlug, request),
  });
  const defaultValues = resourceFormValues(baseline, configuration, options);
  const hasDefaultsChanges = (values: typeof defaultValues.resourceDefaults) =>
    JSON.stringify(values) !==
    JSON.stringify(resourceDefaultsFormValues(configuration.defaults));
  const validate = ({ value }: { value: typeof defaultValues }) => {
    const fields: Record<string, string> = {};
    if (!baseline && !selectedTemplate) {
      fields.sourceTemplateId = 'Choose a resource template to get started.';
    }
    if (!value.name.trim() || value.name.trim().length > 200) {
      fields.name = 'Enter a resource name of 200 characters or fewer.';
    }
    if (value.category === 'Other') {
      if (
        !value.customCategory.trim() ||
        value.customCategory.trim().length > 50
      ) {
        fields.customCategory = 'Enter a category of 50 characters or fewer.';
      }
    } else if (!standardCategories.includes(value.category)) {
      fields.category = 'Choose a category.';
    }
    if (value.description.trim().length > 10_000) {
      fields.description = 'Enter a description of 10,000 characters or fewer.';
    }
    if (
      value.capacity.trim() &&
      (!/^\d+$/.test(value.capacity.trim()) ||
        Number(value.capacity) < 1 ||
        Number(value.capacity) > 2_147_483_647)
    ) {
      fields.capacity =
        'Enter a positive whole number for capacity, or leave it blank.';
    }
    if (!baseline) {
      if (value.spaceId === 'new') {
        if (
          !value.newSpace.name.trim() ||
          value.newSpace.name.trim().length > 200
        ) {
          fields['newSpace.name'] =
            'Enter a location name of 200 characters or fewer.';
        }
        if (value.newSpace.address.trim().length > 500) {
          fields['newSpace.address'] =
            'Enter an address of 500 characters or fewer.';
        }
      } else if (
        !options.spaces.some((space) => String(space.id) === value.spaceId)
      ) {
        fields.spaceId = 'Choose an existing location or add a new location.';
      }
    }
    if (
      !configuration.detailsUnavailableReason &&
      (!baseline || hasDefaultsChanges(value.resourceDefaults))
    ) {
      for (const [path, message] of Object.entries(
        validateResourceDefaultsFields(
          resourceDefaultsFromForm(value.resourceDefaults)
        )
      )) {
        fields[path ? `resourceDefaults.${path}` : 'resourceDefaults'] =
          message;
      }
    }
    if (
      !configuration.formUnavailableReason &&
      (baseline || selectedTemplate) &&
      (!baseline ||
        JSON.stringify(value.definition) !==
          JSON.stringify(configuration.definition))
    ) {
      const errors = value.definition.fields.length
        ? validateFormDefinition(value.definition)
        : ['Add at least one field to the reservation form.'];
      if (errors.length) {
        fields.definition = errors.join(' ');
      }
    }
    return Object.keys(fields).length
      ? { fields, form: [...new Set(Object.values(fields))].join(' ') }
      : undefined;
  };
  const form = useAppForm({
    defaultValues,
    listeners: {
      onChange: () => {
        setSaved(false);
        saveMutation.reset();
      },
    },
    onSubmit: async ({ value }) => {
      if (saveFlowRef.current || (!baseline && !selectedTemplate)) {
        return;
      }
      saveFlowRef.current = true;
      setIsSaving(true);
      setSaved(false);
      try {
        const formUnchanged =
          JSON.stringify(value.definition) ===
          JSON.stringify(configuration.definition);
        const detailsJson =
          serializeResourceDefaults(
            resourceDefaultsFromForm(value.resourceDefaults),
            configuration.detailsJson
          ) ?? '{}';
        const detailsUnchanged =
          detailsJson ===
          (configuration.detailsJson?.trim()
            ? configuration.detailsJson
            : '{}');
        const request: SaveResourceRequest = {
          allowReservationSharing: value.allowReservationSharing,
          approvalMode: value.approvalMode,
          capacity: value.capacity.trim() ? Number(value.capacity) : null,
          category:
            value.category === 'Other'
              ? value.customCategory.trim()
              : value.category,
          description: value.description.trim() || null,
          followTeamCalendar: value.followTeamCalendar,
          isActive: value.isActive,
          isReservable: value.isReservable,
          name: value.name.trim(),
          publicAccess:
            baseline?.parentResourceId != null ? null : value.publicAccess,
          spaceId:
            baseline?.spaceId ??
            (value.spaceId === 'new' ? null : Number(value.spaceId)),
          ...(baseline
            ? {
                expectedConfigId: baseline.config?.id ?? null,
                updatedAt: baseline.updatedAt,
              }
            : {
                sourceTemplateId: selectedTemplate!.id,
                ...(value.spaceId === 'new'
                  ? {
                      newSpace: {
                        address: value.newSpace.address.trim() || null,
                        name: value.newSpace.name.trim(),
                      },
                    }
                  : {}),
              }),
          ...(configuration.formUnavailableReason || formUnchanged
            ? {}
            : {
                formJson: JSON.stringify(value.definition),
                formSchemaVersion: configuration.formSchemaVersion,
              }),
          ...(configuration.detailsUnavailableReason || detailsUnchanged
            ? {}
            : {
                detailsJson,
                detailsSchemaVersion: configuration.detailsSchemaVersion,
              }),
        };
        const result = await saveMutation.mutateAsync(request);
        setBaseline(result);
        form.reset(
          resourceFormValues(result, readConfiguration(result), options)
        );
        queryClient.setQueryData(
          resourceQueryOptions(teamSlug, result.id).queryKey,
          result
        );
        await queryClient.invalidateQueries({
          queryKey: resourcesKey(teamSlug),
        });
        setSaved(true);
        await onSaved(result);
      } catch {
        // Keep entered values and the original concurrency tokens after failure.
      } finally {
        saveFlowRef.current = false;
        setIsSaving(false);
      }
    },
    onSubmitInvalid: ({ formApi }) => {
      void formApi.validate('change');
    },
    validators: { onChange: validate },
  });
  const blocker = useBlocker({
    enableBeforeUnload: () => form.state.isDirty || saveFlowRef.current,
    shouldBlockFn: () => form.state.isDirty,
    withResolver: true,
  });
  const canEdit = Boolean(baseline || selectedTemplate);

  return (
    <section>
      <nav
        aria-label="Breadcrumb"
        className="mb-5 text-sm text-base-content/65"
      >
        <Link
          className="hover:text-primary hover:underline"
          params={{ teamSlug }}
          to="/teams/$teamSlug/resources"
        >
          Resources
        </Link>
        {' / '}
        <span aria-current="page">
          {baseline ? 'Edit resource' : 'Create resource'}
        </span>
      </nav>
      <h2 className="text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
        {baseline ? 'Edit resource' : 'Create resource'}
      </h2>
      <p className="mt-3 max-w-3xl text-base-content/70">
        {baseline
          ? 'Update this resource and its reservation settings. Deactivating it keeps its existing reservations and history.'
          : 'Start with a resource template, then customize the resource, reservation form, opening hours, and rates.'}
      </p>
      {baseline?.config && (
        <p className="mt-3 max-w-3xl text-sm text-base-content/65">
          Configuration version {baseline.config.version}. Saving form, hours,
          or rate changes publishes a new version. Previous versions and
          existing reservations retain their saved configuration.
        </p>
      )}
      {blocker.status === 'blocked' && (
        <section
          aria-label="Unsaved changes"
          className="mt-6 rounded-sm border border-warning bg-warning/10 p-5"
          role="alert"
        >
          <h3 className="font-semibold">Leave this editor?</h3>
          <p className="mt-1">
            Your unsaved changes will be lost. Wait for any save to finish
            before leaving.
          </p>
          <div className="mt-3 flex gap-3">
            <button
              className="btn btn-primary btn-sm"
              onClick={() => blocker.reset()}
              type="button"
            >
              Keep editing
            </button>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(isSubmitting) => (
                <button
                  className="btn btn-outline btn-sm"
                  disabled={isSaving || isSubmitting}
                  onClick={() => blocker.proceed()}
                  type="button"
                >
                  Discard changes
                </button>
              )}
            </form.Subscribe>
          </div>
        </section>
      )}
      <form
        className="mt-8"
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          if (!saveFlowRef.current && !form.state.isSubmitting) {
            if (
              !configuration.detailsUnavailableReason &&
              (!baseline ||
                hasDefaultsChanges(form.state.values.resourceDefaults)) &&
              validateResourceDefaults(
                resourceDefaultsFromForm(form.state.values.resourceDefaults)
              ).length
            ) {
              setEditorView('defaults');
            }
            void form.handleSubmit();
          }
        }}
      >
        <form.Subscribe selector={(state) => state.isSubmitting}>
          {(isSubmitting) => (
            <fieldset className="space-y-6" disabled={isSaving || isSubmitting}>
              {!baseline && (
                <section
                  aria-label="Resource template"
                  className="space-y-3 rounded-sm border border-base-300 bg-base-100 p-5"
                >
                  <h3 className="text-xl font-semibold text-primary">
                    1. Choose a resource template
                  </h3>
                  {selectedTemplate ? (
                    <>
                      <p className="font-semibold">{selectedTemplate.name}</p>
                      {selectedTemplate.description && (
                        <p className="text-sm text-base-content/70">
                          {selectedTemplate.description}
                        </p>
                      )}
                      <p className="text-sm text-base-content/65">
                        This resource has its own copy of the template. Later
                        template changes will not change this resource.
                      </p>
                    </>
                  ) : activeTemplates.length ? (
                    <form.Field name="sourceTemplateId">
                      {(field) => {
                        const hasError =
                          field.state.meta.isTouched &&
                          !field.state.meta.isValid;
                        return (
                          <div>
                            <label
                              className="mb-2 block text-sm font-semibold"
                              htmlFor="resource-template"
                            >
                              Resource template
                            </label>
                            <select
                              aria-describedby={
                                hasError ? 'resource-template-error' : undefined
                              }
                              aria-invalid={hasError || undefined}
                              className="select select-bordered w-full"
                              id="resource-template"
                              name={field.name}
                              onBlur={field.handleBlur}
                              onChange={(event) => {
                                const template = activeTemplates.find(
                                  (item) =>
                                    String(item.id) === event.target.value
                                );
                                if (template) {
                                  const source = readConfiguration(
                                    undefined,
                                    template
                                  );
                                  setSelectedTemplate(template);
                                  field.handleChange(event.target.value);
                                  form.setFieldValue(
                                    'definition',
                                    source.definition
                                  );
                                  form.setFieldValue(
                                    'resourceDefaults',
                                    resourceDefaultsFormValues(source.defaults)
                                  );
                                }
                              }}
                              required
                              value={field.state.value}
                            >
                              <option value="">Choose a template</option>
                              {activeTemplates.map((template) => (
                                <option key={template.id} value={template.id}>
                                  {template.name} (
                                  {template.teamId === null ? 'System' : 'Team'}
                                  )
                                </option>
                              ))}
                            </select>
                            {hasError && (
                              <p
                                className="mt-2 text-sm text-error"
                                id="resource-template-error"
                                role="alert"
                              >
                                {field.state.meta.errors.join(' ')}
                              </p>
                            )}
                          </div>
                        );
                      }}
                    </form.Field>
                  ) : (
                    <p role="status">
                      There are no active resource templates available.{' '}
                      <Link
                        className="link link-primary"
                        params={{ teamSlug }}
                        to="/teams/$teamSlug/resource-templates"
                      >
                        Create a resource template
                      </Link>{' '}
                      before adding a resource.
                    </p>
                  )}
                </section>
              )}
              {canEdit && (
                <>
                  <section
                    aria-label="Resource details"
                    className="space-y-5 rounded-sm border border-base-300 bg-base-100 p-5"
                  >
                    <h3 className="text-xl font-semibold text-primary">
                      {baseline
                        ? 'Resource details'
                        : '2. Describe the resource'}
                    </h3>
                    <div className="grid gap-5 sm:grid-cols-2">
                      <form.AppField name="name">
                        {(field) => <field.TextField label="Resource name" />}
                      </form.AppField>
                      <form.AppField name="category">
                        {(field) => (
                          <field.SelectField
                            label="Category"
                            options={[...standardCategories, 'Other'].map(
                              (category) => ({
                                label: category,
                                value: category,
                              })
                            )}
                            placeholder="Choose a category"
                          />
                        )}
                      </form.AppField>
                      <form.Subscribe
                        selector={(state) => state.values.category}
                      >
                        {(category) =>
                          category === 'Other' && (
                            <form.AppField name="customCategory">
                              {(field) => (
                                <field.TextField
                                  label="Other category"
                                  placeholder="Enter a category"
                                />
                              )}
                            </form.AppField>
                          )
                        }
                      </form.Subscribe>
                      <form.AppField name="capacity">
                        {(field) => (
                          <field.TextField
                            label="Capacity (optional)"
                            placeholder="Leave blank if not applicable"
                          />
                        )}
                      </form.AppField>
                    </div>
                    <form.Field name="description">
                      {(field) => {
                        const hasError =
                          field.state.meta.isTouched &&
                          !field.state.meta.isValid;
                        return (
                          <div>
                            <label
                              className="mb-2 block text-sm font-semibold"
                              htmlFor="resource-description"
                            >
                              Description (optional)
                            </label>
                            <textarea
                              aria-describedby={
                                hasError
                                  ? 'resource-description-error'
                                  : undefined
                              }
                              aria-invalid={hasError || undefined}
                              className="textarea textarea-bordered w-full"
                              id="resource-description"
                              maxLength={10_000}
                              name={field.name}
                              onBlur={field.handleBlur}
                              onChange={(event) =>
                                field.handleChange(event.target.value)
                              }
                              rows={3}
                              value={field.state.value}
                            />
                            {hasError && (
                              <p
                                className="mt-2 text-sm text-error"
                                id="resource-description-error"
                                role="alert"
                              >
                                {field.state.meta.errors.join(' ')}
                              </p>
                            )}
                          </div>
                        );
                      }}
                    </form.Field>
                  </section>
                  <section
                    aria-label="Resource location"
                    className="space-y-4 rounded-sm border border-base-300 bg-base-100 p-5"
                  >
                    <h3 className="text-xl font-semibold text-primary">
                      Location
                    </h3>
                    {baseline ? (
                      <p>
                        {baseline.spaceName}
                        <span className="mt-1 block text-sm text-base-content/65">
                          The resource stays in its current location.
                        </span>
                      </p>
                    ) : (
                      <>
                        <form.AppField name="spaceId">
                          {(field) => (
                            <field.SelectField
                              label="Location"
                              options={[
                                ...options.spaces.map((space) => ({
                                  label: space.name,
                                  value: String(space.id),
                                })),
                                { label: 'Add a new location', value: 'new' },
                              ]}
                              placeholder="Choose a location"
                            />
                          )}
                        </form.AppField>
                        <form.Subscribe
                          selector={(state) => state.values.spaceId}
                        >
                          {(spaceId) =>
                            spaceId === 'new' && (
                              <div className="grid gap-4 sm:grid-cols-2">
                                <form.AppField name="newSpace.name">
                                  {(field) => (
                                    <field.TextField label="Location name" />
                                  )}
                                </form.AppField>
                                <form.AppField name="newSpace.address">
                                  {(field) => (
                                    <field.TextField label="Address (optional)" />
                                  )}
                                </form.AppField>
                                <p className="self-center text-sm text-base-content/65">
                                  The location is created when the resource
                                  saves successfully.
                                </p>
                              </div>
                            )
                          }
                        </form.Subscribe>
                      </>
                    )}
                  </section>
                  <section
                    aria-label="Reservation settings"
                    className="space-y-5 rounded-sm border border-base-300 bg-base-100 p-5"
                  >
                    <h3 className="text-xl font-semibold text-primary">
                      Reservation settings
                    </h3>
                    <div className="grid gap-5 sm:grid-cols-2">
                      <form.AppField name="approvalMode">
                        {(field) => (
                          <field.SelectField
                            label="Reservation approval"
                            options={[
                              { label: 'Manual approval', value: 'manual' },
                              {
                                label: 'Automatic approval',
                                value: 'automatic',
                              },
                            ]}
                          />
                        )}
                      </form.AppField>
                      {baseline?.parentResourceId == null ? (
                        <form.AppField name="publicAccess">
                          {(field) => (
                            <field.SelectField
                              label="Public visibility"
                              options={[
                                {
                                  label: 'Hidden from the public',
                                  value: 'none',
                                },
                                {
                                  label: 'Availability only',
                                  value: 'availability',
                                },
                                {
                                  label: 'Resource details and availability',
                                  value: 'details',
                                },
                              ]}
                            />
                          )}
                        </form.AppField>
                      ) : (
                        <p className="self-center text-sm text-base-content/65">
                          Public visibility is inherited from the parent
                          resource.
                        </p>
                      )}
                    </div>
                    {(
                      [
                        ['isActive', 'Active resource'],
                        [
                          'isReservable',
                          'Allow reservations for this resource',
                        ],
                        ['followTeamCalendar', 'Follow the team calendar'],
                        [
                          'allowReservationSharing',
                          'Allow reservation sharing',
                        ],
                      ] as const
                    ).map(([name, label]) => (
                      <form.Field key={name} name={name}>
                        {(field) => (
                          <label className="flex items-center gap-3">
                            <input
                              checked={field.state.value}
                              className="checkbox checkbox-primary"
                              name={field.name}
                              onBlur={field.handleBlur}
                              onChange={(event) =>
                                field.handleChange(event.target.checked)
                              }
                              type="checkbox"
                            />
                            {label}
                          </label>
                        )}
                      </form.Field>
                    ))}
                    <p className="text-sm text-base-content/65">
                      Clear Active resource and save to deactivate it. Check it
                      again and save to reactivate. Existing reservations and
                      history are kept.
                    </p>
                  </section>
                  <div
                    aria-label="Configuration view"
                    className="flex flex-wrap gap-2"
                    role="group"
                  >
                    <button
                      aria-pressed={editorView === 'build'}
                      className={`btn btn-sm ${editorView === 'build' ? 'btn-primary' : 'btn-ghost'}`}
                      onClick={() => setEditorView('build')}
                      type="button"
                    >
                      Reservation form
                    </button>
                    <button
                      aria-pressed={editorView === 'preview'}
                      className={`btn btn-sm ${editorView === 'preview' ? 'btn-primary' : 'btn-ghost'}`}
                      onClick={() => setEditorView('preview')}
                      type="button"
                    >
                      Preview form
                    </button>
                    <button
                      aria-pressed={editorView === 'defaults'}
                      className={`btn btn-sm ${editorView === 'defaults' ? 'btn-primary' : 'btn-ghost'}`}
                      onClick={() => setEditorView('defaults')}
                      type="button"
                    >
                      Hours and rates
                    </button>
                  </div>
                  {editorView !== 'defaults' &&
                    configuration.formUnavailableReason && (
                      <div
                        className="rounded-sm border border-warning bg-warning/10 p-4"
                        role="status"
                      >
                        <p>{configuration.formUnavailableReason}</p>
                        <p className="mt-2 text-sm">
                          The saved form will be preserved when you save other
                          resource changes.
                        </p>
                      </div>
                    )}
                  {editorView === 'build' &&
                    !configuration.formUnavailableReason && (
                      <form.Field name="definition">
                        {(field) => (
                          <FormBuilder
                            disabled={isSaving || isSubmitting}
                            onChange={field.handleChange}
                            value={field.state.value}
                          />
                        )}
                      </form.Field>
                    )}
                  {editorView === 'defaults' && (
                    <ResourceDefaultsEditor
                      context="resource"
                      disabled={isSaving || isSubmitting}
                      fields="resourceDefaults"
                      form={form}
                      readOnly={false}
                      unavailableReason={configuration.detailsUnavailableReason}
                    />
                  )}
                  <div className="flex flex-wrap items-center gap-4">
                    <button className="btn btn-primary" type="submit">
                      {isSaving || isSubmitting
                        ? 'Saving…'
                        : baseline
                          ? 'Save changes'
                          : 'Create resource'}
                    </button>
                    <Link
                      className="link link-primary"
                      params={{ teamSlug }}
                      to="/teams/$teamSlug/resources"
                    >
                      Back to resources
                    </Link>
                  </div>
                </>
              )}
            </fieldset>
          )}
        </form.Subscribe>
        <form.Subscribe
          selector={(state) =>
            [state.errors, state.isDirty, state.submissionAttempts] as const
          }
        >
          {([errors, isDirty, submissionAttempts]) => (
            <div aria-live="polite" className="mt-5">
              {submissionAttempts > 0 && errors.length > 0 && (
                <p className="text-error" role="alert">
                  {[...new Set(errors)].join(' ')}
                </p>
              )}
              {saveMutation.isError && (
                <p className="text-error" role="alert">
                  {resourceErrorMessage(saveMutation.error)}
                </p>
              )}
              {saved && !isDirty && (
                <p className="text-success" role="status">
                  Resource saved.
                </p>
              )}
              {isDirty && (
                <p className="mt-2 text-sm text-base-content/65">
                  You have unsaved changes.
                </p>
              )}
            </div>
          )}
        </form.Subscribe>
      </form>
      {canEdit &&
        editorView === 'preview' &&
        !configuration.formUnavailableReason && (
          <form.Subscribe selector={(state) => state.values.definition}>
            {(definition) => (
              <div className="mt-6">
                <FormPreview definition={definition} />
              </div>
            )}
          </form.Subscribe>
        )}
    </section>
  );
}
