import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useBlocker } from '@tanstack/react-router';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
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
import { FieldWrapper } from '@/shared/forms/fieldWrapper.tsx';
import { useAppForm } from '@/shared/forms/formContext.tsx';

const standardCategories = ['Room', 'Bench', 'Equipment'];

function suggestSlug(name: string) {
  return name
    .normalize('NFKD')
    .replaceAll(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replaceAll(/[^\da-z]+/g, '-')
    .replaceAll(/^-+|-+$/g, '')
    .slice(0, 120)
    .replaceAll(/-+$/g, '');
}

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
    publicAccess: resource?.publicAccess ?? 'availability',
    resourceDefaults: resourceDefaultsFormValues(configuration.defaults),
    slug: resource?.slug ?? '',
    sourceTemplateId: '',
    spaceId: resource
      ? String(resource.spaceId)
      : options.spaces.length === 1
        ? String(options.spaces[0].id)
        : '',
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
  const formId = useId();
  // Keep the draft and its concurrency tokens together when queries refetch.
  const [baseline, setBaseline] = useState(resource);
  const [selectedTemplate, setSelectedTemplate] = useState<ResourceTemplate>();
  const configuration = useMemo(
    () => readConfiguration(baseline, selectedTemplate),
    [baseline, selectedTemplate]
  );
  const [editorView, setEditorView] = useState<'details' | 'build' | 'preview'>(
    'details'
  );
  const [saved, setSaved] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const saveFlowRef = useRef(false);
  const slugEditedRef = useRef(Boolean(resource));
  const activeTemplates = options.templates.filter(
    (template) => template.isActive
  );
  const onlySpace = options.spaces.length === 1 ? options.spaces[0] : undefined;
  const onlySpaceId = onlySpace ? String(onlySpace.id) : undefined;
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
    if (!value.slug) {
      fields.slug = 'Enter a resource slug.';
    } else if (value.slug.length > 120) {
      fields.slug = 'Use 120 characters or fewer.';
    } else if (!/^[\da-z]+(?:-[\da-z]+)*$/.test(value.slug)) {
      fields.slug =
        'Use lowercase letters, numbers, and single hyphens between words.';
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
    if (
      !baseline &&
      !options.spaces.some((space) => String(space.id) === value.spaceId)
    ) {
      fields.spaceId = 'Choose an available location for this team.';
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
      if (
        saveFlowRef.current ||
        (!baseline &&
          (!selectedTemplate ||
            !options.spaces.some(
              (space) => String(space.id) === value.spaceId
            )))
      ) {
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
          slug: value.slug,
          spaceId: baseline?.spaceId ?? Number(value.spaceId),
          ...(baseline
            ? {
                expectedConfigId: baseline.config?.id ?? null,
                updatedAt: baseline.updatedAt,
              }
            : {
                sourceTemplateId: selectedTemplate!.id,
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
      const errors = validate({ value: formApi.state.values })?.fields;
      if (errors) {
        setEditorView(
          Object.keys(errors).some(
            (key) => key !== 'definition' && !key.startsWith('resourceDefaults')
          )
            ? 'details'
            : errors.definition
              ? 'build'
              : 'details'
        );
      }
    },
    validators: { onChange: validate },
  });
  useEffect(() => {
    if (!baseline && onlySpaceId && form.state.values.spaceId !== onlySpaceId) {
      form.setFieldValue('spaceId', onlySpaceId);
    }
  }, [baseline, form, onlySpaceId]);
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
        className="mb-5 text-sm text-base-content/75"
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
      <div className="flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h2 className="text-2xl font-semibold tracking-tight text-primary">
            {baseline ? 'Edit resource' : 'Create resource'}
          </h2>
          <p className="mt-2 max-w-2xl text-base-content/80">
            {baseline
              ? 'Update this resource and its reservation settings.'
              : selectedTemplate
                ? 'Set the resource details and reservation settings, then review your form.'
                : 'Choose a template to get started.'}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-3 self-start">
          <Link
            className="btn btn-ghost"
            params={{ teamSlug }}
            to="/teams/$teamSlug/resources"
          >
            Cancel
          </Link>
          {canEdit && (
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(isSubmitting) => (
                <button
                  className="btn btn-primary"
                  disabled={
                    isSaving ||
                    isSubmitting ||
                    (!baseline && options.spaces.length === 0)
                  }
                  form={formId}
                  type="submit"
                >
                  {isSaving || isSubmitting
                    ? 'Saving…'
                    : baseline
                      ? 'Save changes'
                      : 'Create resource'}
                </button>
              )}
            </form.Subscribe>
          )}
        </div>
      </div>
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
        className="mt-6"
        id={formId}
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
              setEditorView('details');
            }
            void form.handleSubmit();
          }
        }}
      >
        <form.Subscribe selector={(state) => state.isSubmitting}>
          {(isSubmitting) => (
            <fieldset className="space-y-6" disabled={isSaving || isSubmitting}>
              {!baseline && (
                <section aria-label="Resource setup" className="max-w-3xl">
                  {options.spaces.length === 0 && (
                    <p className="alert alert-warning mb-4" role="status">
                      No active locations are available for this team. A
                      location must be made available to the team before a
                      resource can be created.
                    </p>
                  )}
                  {!baseline && (
                    <section
                      aria-label="Resource template"
                      className="space-y-2"
                    >
                      {selectedTemplate ? (
                        <dl className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
                          <dt className="text-base-content/75">Template</dt>
                          <dd className="min-w-0">
                            <p className="font-semibold">
                              {selectedTemplate.name}
                            </p>
                          </dd>
                        </dl>
                      ) : activeTemplates.length ? (
                        <form.Field name="sourceTemplateId">
                          {(field) => {
                            const hasError =
                              field.state.meta.isTouched &&
                              !field.state.meta.isValid;
                            return (
                              <div>
                                <label
                                  className="mb-2 block text-sm font-semibold text-base-content"
                                  htmlFor="resource-template"
                                >
                                  Resource template
                                </label>
                                <select
                                  aria-describedby={
                                    hasError
                                      ? 'resource-template-error'
                                      : undefined
                                  }
                                  aria-invalid={hasError || undefined}
                                  className="select select-bordered w-full"
                                  disabled={options.spaces.length === 0}
                                  id="resource-template"
                                  name={field.name}
                                  onBlur={field.handleBlur}
                                  onChange={(event) => {
                                    if (options.spaces.length === 0) {
                                      return;
                                    }
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
                                        resourceDefaultsFormValues(
                                          source.defaults
                                        )
                                      );
                                    }
                                  }}
                                  required
                                  value={field.state.value}
                                >
                                  <option value="">Choose a template</option>
                                  {activeTemplates.map((template) => (
                                    <option
                                      key={template.id}
                                      value={template.id}
                                    >
                                      {template.name} (
                                      {template.teamId === null
                                        ? 'System'
                                        : 'Team'}
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
                </section>
              )}
              <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2">
                {canEdit && (
                  <div
                    aria-label="Configuration view"
                    className="tabs tabs-border shrink-0"
                    onKeyDown={(event) => {
                      if (
                        !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(
                          event.key
                        )
                      ) {
                        return;
                      }
                      const tabs = Array.from(
                        event.currentTarget.querySelectorAll<HTMLButtonElement>(
                          '[role="tab"]'
                        )
                      );
                      const current = tabs.indexOf(
                        document.activeElement as HTMLButtonElement
                      );
                      const next =
                        event.key === 'Home'
                          ? 0
                          : event.key === 'End'
                            ? tabs.length - 1
                            : (current +
                                (event.key === 'ArrowRight' ? 1 : -1) +
                                tabs.length) %
                              tabs.length;
                      event.preventDefault();
                      tabs[next]?.focus();
                      tabs[next]?.click();
                    }}
                    role="tablist"
                  >
                    {(
                      [
                        { label: 'Resource details', value: 'details' },
                        { label: 'Customize form', value: 'build' },
                        { label: 'Preview form', value: 'preview' },
                      ] as const
                    ).map(({ label, value }) => (
                      <button
                        aria-controls={`${formId}-${value === 'details' ? 'details-panel' : 'panel'}`}
                        aria-selected={editorView === value}
                        className={`tab font-semibold ${editorView === value ? 'tab-active text-primary' : ''}`}
                        id={`${formId}-${value}`}
                        key={value}
                        onClick={() => setEditorView(value)}
                        role="tab"
                        tabIndex={editorView === value ? 0 : -1}
                        type="button"
                      >
                        {label}
                      </button>
                    ))}
                  </div>
                )}
                <form.Subscribe
                  selector={(state) =>
                    [
                      state.errors,
                      state.isDirty,
                      state.submissionAttempts,
                    ] as const
                  }
                >
                  {([errors, isDirty, submissionAttempts]) => (
                    <div
                      aria-live="polite"
                      className="min-w-0 text-sm sm:ml-auto sm:max-w-xl sm:text-right"
                    >
                      {submissionAttempts > 0 && errors.length > 0 ? (
                        <p className="text-error" role="alert">
                          {[...new Set(errors)].join(' ')}
                        </p>
                      ) : saveMutation.isError ? (
                        <p className="text-error" role="alert">
                          {resourceErrorMessage(saveMutation.error)}
                        </p>
                      ) : saved && !isDirty ? (
                        <p className="text-success" role="status">
                          Resource saved.
                        </p>
                      ) : baseline && isDirty ? (
                        <p className="text-base-content/75">
                          You have unsaved changes.
                        </p>
                      ) : null}
                    </div>
                  )}
                </form.Subscribe>
              </div>
              {canEdit && (
                <>
                  <div
                    aria-labelledby={`${formId}-details`}
                    className="grid items-start gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]"
                    hidden={editorView !== 'details'}
                    id={`${formId}-details-panel`}
                    role="tabpanel"
                  >
                    <section
                      aria-label="Resource details"
                      className="min-w-0 space-y-5 rounded-sm border border-base-300 bg-base-200 p-4 shadow-sm sm:p-6"
                    >
                      <h3 className="text-xl font-semibold text-primary">
                        Resource details
                      </h3>
                      <section
                        aria-label="Resource location"
                        className="space-y-2"
                      >
                        {baseline || onlySpace ? (
                          <dl className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                            <dt className="text-sm text-base-content/75">
                              Location
                            </dt>
                            <dd>{baseline?.space.name ?? onlySpace?.name}</dd>
                          </dl>
                        ) : (
                          <form.AppField name="spaceId">
                            {(field) => {
                              const hasError =
                                field.state.meta.isTouched &&
                                !field.state.meta.isValid;
                              const space = options.spaces.find(
                                (space) =>
                                  String(space.id) === field.state.value
                              );
                              return (
                                <>
                                  <FieldWrapper
                                    controlId="resource-location"
                                    label="Location"
                                  >
                                    <select
                                      aria-describedby={`resource-location-help${hasError ? ' resource-location-error' : ''}`}
                                      aria-invalid={hasError || undefined}
                                      className={`select select-bordered w-full ${hasError ? 'select-error' : ''}`}
                                      id="resource-location"
                                      name={field.name}
                                      onBlur={field.handleBlur}
                                      onChange={(event) =>
                                        field.handleChange(event.target.value)
                                      }
                                      required
                                      value={field.state.value}
                                    >
                                      <option value="">Choose a space</option>
                                      {options.spaces.map((space) => (
                                        <option
                                          key={space.id}
                                          value={String(space.id)}
                                        >
                                          {space.name}
                                        </option>
                                      ))}
                                    </select>
                                  </FieldWrapper>
                                  <div
                                    className="space-y-1 text-sm text-base-content/75"
                                    id="resource-location-help"
                                  >
                                    {space?.description?.trim() && (
                                      <p className="whitespace-pre-line">
                                        {space.description.trim()}
                                      </p>
                                    )}
                                    {space?.address?.trim() && (
                                      <p className="whitespace-pre-line">
                                        {space.address.trim()}
                                      </p>
                                    )}
                                  </div>
                                  {hasError && (
                                    <p
                                      className="text-sm text-error"
                                      id="resource-location-error"
                                      role="alert"
                                    >
                                      {field.state.meta.errors
                                        .filter(Boolean)
                                        .join(', ')}
                                    </p>
                                  )}
                                </>
                              );
                            }}
                          </form.AppField>
                        )}
                      </section>
                      <div className="grid gap-5 sm:grid-cols-2">
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
                      </div>
                      <div className="grid gap-5 sm:grid-cols-2">
                        <form.Subscribe
                          selector={(state) => state.values.category}
                        >
                          {(category) => (
                            <form.AppField
                              listeners={{
                                onChange: ({ value }) => {
                                  if (!baseline && !slugEditedRef.current) {
                                    form.setFieldValue(
                                      'slug',
                                      suggestSlug(value)
                                    );
                                  }
                                },
                              }}
                              name="name"
                            >
                              {(field) => (
                                <field.TextField
                                  label="Resource name"
                                  placeholder={
                                    category === 'Room'
                                      ? 'Enter a room name or number'
                                      : 'Enter the resource name'
                                  }
                                />
                              )}
                            </form.AppField>
                          )}
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
                      <form.AppField name="slug">
                        {(field) => {
                          const hasError =
                            field.state.meta.isTouched &&
                            !field.state.meta.isValid;
                          return (
                            <FieldWrapper
                              controlId="resource-slug"
                              label="Resource slug"
                            >
                              <input
                                aria-describedby={`resource-slug-help${hasError ? ' resource-slug-error' : ''}`}
                                aria-invalid={hasError || undefined}
                                autoCapitalize="none"
                                autoComplete="off"
                                className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                                id="resource-slug"
                                maxLength={120}
                                name={field.name}
                                onBlur={field.handleBlur}
                                onChange={(event) => {
                                  slugEditedRef.current = true;
                                  field.handleChange(event.target.value);
                                }}
                                required
                                spellCheck={false}
                                type="text"
                                value={field.state.value}
                              />
                              <p
                                className="mt-2 text-sm text-base-content/75"
                                id="resource-slug-help"
                              >
                                Use lowercase letters, numbers, and single
                                hyphens between words. You can change the
                                suggested slug. It must be unique within this
                                team’s resources.
                              </p>
                            </FieldWrapper>
                          );
                        }}
                      </form.AppField>
                      <form.Field name="description">
                        {(field) => {
                          const hasError =
                            field.state.meta.isTouched &&
                            !field.state.meta.isValid;
                          return (
                            <div>
                              <label
                                className="mb-2 block text-sm font-semibold text-base-content"
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
                      <div className="border-t border-base-300 pt-5">
                        <ResourceDefaultsEditor
                          context="resource"
                          disabled={isSaving || isSubmitting}
                          embedded
                          fields="resourceDefaults"
                          form={form}
                          readOnly={false}
                          unavailableReason={
                            configuration.detailsUnavailableReason
                          }
                        />
                      </div>
                    </section>
                    <section
                      aria-label="Reservation settings"
                      className="min-w-0 space-y-5 rounded-sm border border-base-300 bg-base-200 p-4 shadow-sm sm:p-6"
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
                          <p className="self-center text-sm text-base-content/75">
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
                            <label className="flex items-center gap-3 font-semibold text-base-content">
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
                      <p className="text-sm text-base-content/75">
                        Clear Active resource and save to deactivate it. Check
                        it again and save to reactivate. Existing reservations
                        and history are kept.
                      </p>
                    </section>
                  </div>
                </>
              )}
            </fieldset>
          )}
        </form.Subscribe>
      </form>
      {canEdit && (
        <form.Subscribe selector={(state) => state.isSubmitting}>
          {(isSubmitting) => (
            <fieldset
              className="min-w-0 rounded-sm border border-base-300 bg-base-200 p-4 shadow-sm sm:p-6"
              disabled={isSaving || isSubmitting}
              hidden={editorView === 'details'}
            >
              <div
                aria-labelledby={`${formId}-${editorView}`}
                id={`${formId}-panel`}
                role="tabpanel"
              >
                {configuration.formUnavailableReason && (
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
                          embedded
                          onChange={field.handleChange}
                          value={field.state.value}
                        />
                      )}
                    </form.Field>
                  )}
                {editorView === 'preview' &&
                  !configuration.formUnavailableReason && (
                    <form.Subscribe
                      selector={(state) => state.values.definition}
                    >
                      {(definition) => (
                        <FormPreview definition={definition} embedded />
                      )}
                    </form.Subscribe>
                  )}
              </div>
            </fieldset>
          )}
        </form.Subscribe>
      )}
    </section>
  );
}
