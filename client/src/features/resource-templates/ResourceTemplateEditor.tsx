import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useBlocker } from '@tanstack/react-router';
import { useId, useMemo, useRef, useState } from 'react';
import { FormBuilder } from './FormBuilder.tsx';
import { FormPreview } from './FormPreview.tsx';
import { validateFormDefinition } from './formDefinition.ts';
import type { FormDefinition } from './models/FormDefinition.ts';
import type { ResourceTemplate } from './models/ResourceTemplate.ts';
import type { SaveResourceTemplateRequest } from './models/SaveResourceTemplateRequest.ts';
import { ResourceDefaultsEditor } from '@/features/resource-defaults/ResourceDefaultsEditor.tsx';
import {
  parseResourceDefaultsJson,
  resourceDefaultsFormValues,
  resourceDefaultsFromForm,
  serializeResourceDefaults,
  validateResourceDefaults,
  validateResourceDefaultsFields,
} from '@/features/resource-defaults/resourceDefaults.ts';
import { useAppForm } from '@/shared/forms/formContext.tsx';
import {
  createResourceTemplate,
  resourceTemplateErrorMessage,
  resourceTemplateQueryOptions,
  resourceTemplatesKey,
  updateResourceTemplate,
} from '@/queries/resourceTemplates.ts';

export function ResourceTemplateEditor({
  definition,
  onSaved,
  teamSlug,
  template,
  viewOnly = false,
}: {
  definition: FormDefinition;
  onSaved?: (saved: ResourceTemplate) => void | Promise<void>;
  teamSlug?: string;
  template?: ResourceTemplate;
  viewOnly?: boolean;
}) {
  const queryClient = useQueryClient();
  const formId = useId();
  const [baseline, setBaseline] = useState({ definition, template });
  const currentTemplate = baseline.template;
  const defaultsSource = useMemo(
    () => parseResourceDefaultsJson(currentTemplate?.resourceDefaultsJson),
    [currentTemplate?.resourceDefaultsJson]
  );
  const isArchived = currentTemplate?.isActive === false;
  const isSystemTemplate =
    Boolean(teamSlug) && currentTemplate?.teamId === null;
  const isReadOnly = viewOnly || isArchived || isSystemTemplate;
  const Container = teamSlug ? 'section' : 'main';
  const Heading = teamSlug ? 'h2' : 'h1';
  const listLink = teamSlug
    ? {
        params: { teamSlug },
        to: '/teams/$teamSlug/resource-templates' as const,
      }
    : { to: '/admin/resource-templates' as const };
  const editLink =
    currentTemplate && !isArchived && !isSystemTemplate
      ? teamSlug
        ? {
            params: { teamSlug, templateId: String(currentTemplate.id) },
            to: '/teams/$teamSlug/resource-templates/$templateId' as const,
          }
        : {
            params: { templateId: String(currentTemplate.id) },
            to: '/admin/resource-templates/$templateId' as const,
          }
      : undefined;
  const [editorView, setEditorView] = useState<
    'build' | 'preview' | 'defaults'
  >('build');
  const activeView =
    isReadOnly && editorView === 'build' ? 'preview' : editorView;
  const [saved, setSaved] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const saveFlowRef = useRef(false);
  const saveMutation = useMutation({
    mutationFn: (request: SaveResourceTemplateRequest) =>
      currentTemplate
        ? updateResourceTemplate(currentTemplate.id, request, teamSlug)
        : createResourceTemplate(request, teamSlug),
  });
  const defaultValues = {
    definition: baseline.definition,
    description: currentTemplate?.description ?? '',
    isActive: currentTemplate?.isActive ?? true,
    name: currentTemplate?.name ?? '',
    resourceDefaults: resourceDefaultsFormValues(defaultsSource.defaults),
  };
  const validate = ({ value }: { value: typeof defaultValues }) => {
    const fields: Record<string, string> = {};
    if (!value.name.trim() || value.name.trim().length > 200) {
      fields.name = 'Enter a template name of 200 characters or fewer.';
    }
    if (value.description.trim().length > 2000) {
      fields.description = 'Enter a description of 2,000 characters or fewer.';
    }
    const defaultsErrors = validateResourceDefaultsFields(
      resourceDefaultsFromForm(value.resourceDefaults)
    );
    for (const [path, message] of Object.entries(defaultsErrors)) {
      fields[path ? `resourceDefaults.${path}` : 'resourceDefaults'] = message;
    }
    const definitionErrors =
      value.definition.fields.length === 0
        ? ['Add at least one form field before saving the template.']
        : validateFormDefinition(value.definition);
    if (definitionErrors.length) {
      fields.definition = definitionErrors.join(' ');
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
      if (isReadOnly || saveFlowRef.current) {
        return;
      }
      saveFlowRef.current = true;
      setIsSaving(true);
      setSaved(false);
      try {
        const result = await saveMutation.mutateAsync({
          description: value.description.trim() || null,
          formJson: JSON.stringify(value.definition),
          formSchemaVersion: currentTemplate?.formSchemaVersion ?? 1,
          isActive: value.isActive,
          name: value.name.trim(),
          ...(defaultsSource.unavailableReason
            ? {}
            : {
                resourceDefaultsJson: serializeResourceDefaults(
                  resourceDefaultsFromForm(value.resourceDefaults),
                  currentTemplate?.resourceDefaultsJson
                ),
              }),
          updatedAt: currentTemplate?.updatedAt,
        });
        setBaseline({ definition: value.definition, template: result });
        form.reset({
          ...value,
          description: result.description ?? '',
          isActive: result.isActive,
          name: result.name,
          resourceDefaults: resourceDefaultsFormValues(
            parseResourceDefaultsJson(result.resourceDefaultsJson).defaults
          ),
        });
        queryClient.setQueryData(
          resourceTemplateQueryOptions(result.id, teamSlug).queryKey,
          result
        );
        await queryClient.invalidateQueries({
          queryKey: resourceTemplatesKey(teamSlug),
        });
        setSaved(true);
        await onSaved?.(result);
      } catch {
        // Keep the entered values available when saving fails.
      } finally {
        saveFlowRef.current = false;
        setIsSaving(false);
      }
    },
    onSubmitInvalid: ({ formApi }) => {
      // Refresh errors for array fields mounted after the last change validation.
      void formApi.validate('change');
    },
    validators: {
      // TanStack also runs change validation on submit, including untouched values.
      onChange: validate,
    },
  });
  const blocker = useBlocker({
    enableBeforeUnload: () => form.state.isDirty || saveFlowRef.current,
    shouldBlockFn: () => form.state.isDirty,
    withResolver: true,
  });

  return (
    <Container
      className={teamSlug ? undefined : 'content-container py-4 sm:py-8'}
    >
      <nav
        aria-label="Breadcrumb"
        className="mb-5 text-sm text-base-content/65"
      >
        {!teamSlug && (
          <>
            <Link className="hover:text-primary hover:underline" to="/admin">
              Site admin
            </Link>
            {' / '}
          </>
        )}
        <Link className="hover:text-primary hover:underline" {...listLink}>
          Resource templates
        </Link>
        {' / '}
        <span aria-current="page">
          {isReadOnly
            ? 'View template'
            : currentTemplate
              ? 'Edit template'
              : 'Create template'}
        </span>
      </nav>
      <div className="flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <Heading className="text-2xl font-semibold tracking-tight text-primary">
            {isReadOnly
              ? 'View resource template'
              : currentTemplate
                ? 'Edit resource template'
                : 'Create resource template'}
          </Heading>
          <p className="mt-3 max-w-2xl text-base-content/70">
            {isArchived
              ? 'Review this archived template.'
              : isSystemTemplate
                ? 'Preview this system template or duplicate it into your team to customize it.'
                : viewOnly
                  ? 'Review the form and its resource defaults.'
                  : 'Create a reusable request form and starting settings for resources.'}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-3 self-start">
          <Link className="btn btn-ghost" {...listLink}>
            {isReadOnly ? 'Back to resource templates' : 'Cancel'}
          </Link>
          {isReadOnly ? (
            editLink && (
              <Link className="btn btn-primary" search={{}} {...editLink}>
                Edit template
              </Link>
            )
          ) : (
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(isSubmitting) => (
                <button
                  className="btn btn-primary"
                  disabled={isSaving || isSubmitting}
                  form={formId}
                  type="submit"
                >
                  {isSaving || isSubmitting
                    ? 'Saving…'
                    : currentTemplate
                      ? 'Save template'
                      : 'Create template'}
                </button>
              )}
            </form.Subscribe>
          )}
        </div>
      </div>
      {currentTemplate && (
        <div className="mt-4 flex flex-wrap items-center gap-3 text-sm text-base-content/65">
          <span className="badge badge-soft">
            Version {currentTemplate.formSchemaVersion}
          </span>
          <p>
            {isReadOnly
              ? isArchived
                ? 'Duplicate this archived template to make changes.'
                : isSystemTemplate
                  ? 'System templates are read-only.'
                  : 'Choose Edit template to make changes.'
              : 'Form and default changes create a new version. Existing resources keep their saved settings.'}
          </p>
        </div>
      )}

      {blocker.status === 'blocked' && (
        <section
          aria-label="Unsaved changes"
          className="mt-6 rounded-sm border border-warning bg-warning/10 p-5"
          role="alert"
        >
          <h2 className="font-semibold">Leave this editor?</h2>
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

      <form.Subscribe selector={(state) => state.isSubmitting}>
        {(isSubmitting) => (
          <fieldset
            className="mt-8 min-w-0 space-y-6"
            disabled={isSaving || isSubmitting}
          >
            <form
              id={formId}
              noValidate
              onSubmit={(event) => {
                event.preventDefault();
                if (
                  !isReadOnly &&
                  !saveFlowRef.current &&
                  !form.state.isSubmitting
                ) {
                  if (
                    validateResourceDefaults(
                      resourceDefaultsFromForm(
                        form.state.values.resourceDefaults
                      )
                    ).length
                  ) {
                    setEditorView('defaults');
                  }
                  void form.handleSubmit();
                }
              }}
            >
              <fieldset
                className="space-y-5 rounded-sm border border-base-300 bg-base-200 p-4 shadow-sm sm:p-6"
                disabled={isReadOnly}
              >
                <h3 className="text-xl font-semibold text-primary">
                  Template details
                </h3>
                <div className="flex flex-col gap-5 sm:flex-row sm:items-end">
                  <form.Field name="name">
                    {(field) => {
                      const hasError =
                        field.state.meta.isTouched && !field.state.meta.isValid;
                      return (
                        <div className="grow">
                          <label
                            className="mb-2 block text-sm font-semibold"
                            htmlFor="template-name"
                          >
                            Template name
                          </label>
                          <input
                            aria-describedby={
                              hasError ? 'template-name-error' : undefined
                            }
                            aria-invalid={hasError || undefined}
                            className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                            id="template-name"
                            maxLength={200}
                            name={field.name}
                            onBlur={field.handleBlur}
                            onChange={(event) =>
                              field.handleChange(event.target.value)
                            }
                            required
                            value={field.state.value}
                          />
                          {hasError && (
                            <p
                              className="mt-2 text-sm text-error"
                              id="template-name-error"
                              role="alert"
                            >
                              {field.state.meta.errors.join(' ')}
                            </p>
                          )}
                        </div>
                      );
                    }}
                  </form.Field>
                  <form.Field name="isActive">
                    {(field) => (
                      <label className="flex items-center gap-3 py-3">
                        <input
                          checked={field.state.value}
                          className="checkbox checkbox-primary"
                          onChange={(event) =>
                            field.handleChange(event.target.checked)
                          }
                          type="checkbox"
                        />
                        Active template
                      </label>
                    )}
                  </form.Field>
                </div>
                <form.Field name="description">
                  {(field) => {
                    const hasError =
                      field.state.meta.isTouched && !field.state.meta.isValid;
                    return (
                      <div>
                        <label
                          className="mb-2 block text-sm font-semibold"
                          htmlFor="template-description"
                        >
                          Description
                        </label>
                        <textarea
                          aria-describedby={`template-description-help${hasError ? ' template-description-error' : ''}`}
                          aria-invalid={hasError || undefined}
                          className={`textarea textarea-bordered w-full ${hasError ? 'textarea-error' : ''}`}
                          id="template-description"
                          maxLength={2000}
                          name={field.name}
                          onBlur={field.handleBlur}
                          onChange={(event) =>
                            field.handleChange(event.target.value)
                          }
                          rows={3}
                          value={field.state.value}
                        />
                        <p
                          className="mt-2 text-sm text-base-content/65"
                          id="template-description-help"
                        >
                          Optional. Describe what this template is for in 2,000
                          characters or fewer.
                        </p>
                        {hasError && (
                          <p
                            className="mt-2 text-sm text-error"
                            id="template-description-error"
                            role="alert"
                          >
                            {field.state.meta.errors.join(' ')}
                          </p>
                        )}
                      </div>
                    );
                  }}
                </form.Field>
                {currentTemplate && !isReadOnly && (
                  <p className="text-sm text-base-content/65">
                    Clear Active template and save to archive this version. Save
                    form or default changes before archiving.
                  </p>
                )}
              </fieldset>
            </form>
            <section
              aria-label="Template editor"
              className="min-w-0 rounded-sm border border-base-300 bg-base-200 p-4 shadow-sm sm:p-6"
            >
              <div
                aria-label="Editor view"
                className="tabs tabs-border mb-6"
                onKeyDown={(event) => {
                  if (
                    !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(
                      event.key
                    )
                  )
                    {return;}
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
                {[
                  ...(!isReadOnly
                    ? [{ label: 'Build form', value: 'build' as const }]
                    : []),
                  { label: 'Preview form', value: 'preview' as const },
                  { label: 'Resource defaults', value: 'defaults' as const },
                ].map(({ label, value }) => (
                  <button
                    aria-controls={`${formId}-panel`}
                    aria-selected={activeView === value}
                    className={`tab font-semibold ${activeView === value ? 'tab-active text-primary' : ''}`}
                    id={`${formId}-${value}`}
                    key={value}
                    onClick={() => setEditorView(value)}
                    role="tab"
                    tabIndex={activeView === value ? 0 : -1}
                    type="button"
                  >
                    {label}
                  </button>
                ))}
              </div>
              <div
                aria-labelledby={`${formId}-${activeView}`}
                id={`${formId}-panel`}
                role="tabpanel"
              >
                {activeView === 'build' && (
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
                {activeView === 'defaults' && (
                  <ResourceDefaultsEditor
                    context="template"
                    disabled={isSaving || isSubmitting}
                    embedded
                    fields="resourceDefaults"
                    form={form}
                    readOnly={isReadOnly}
                    unavailableReason={defaultsSource.unavailableReason}
                  />
                )}
                {activeView === 'preview' && (
                  <form.Subscribe selector={(state) => state.values.definition}>
                    {(currentDefinition) => (
                      <FormPreview definition={currentDefinition} embedded />
                    )}
                  </form.Subscribe>
                )}
              </div>
            </section>
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
                {resourceTemplateErrorMessage(saveMutation.error)}
              </p>
            )}
            {saved && !isDirty && (
              <p className="text-success" role="status">
                Template saved.
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
    </Container>
  );
}
