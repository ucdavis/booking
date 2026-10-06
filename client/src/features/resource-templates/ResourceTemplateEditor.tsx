import { useForm } from '@tanstack/react-form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useBlocker } from '@tanstack/react-router';
import { useMemo, useRef, useState } from 'react';
import { FormBuilder } from './FormBuilder.tsx';
import { FormPreview } from './FormPreview.tsx';
import { validateFormDefinition } from './formDefinition.ts';
import type { FormDefinition } from './models/FormDefinition.ts';
import type { ResourceTemplate } from './models/ResourceTemplate.ts';
import type { SaveResourceTemplateRequest } from './models/SaveResourceTemplateRequest.ts';
import { ResourceDefaultsEditor } from '@/features/resource-defaults/ResourceDefaultsEditor.tsx';
import {
  parseResourceDefaultsJson,
  serializeResourceDefaults,
  validateResourceDefaults,
} from '@/features/resource-defaults/resourceDefaults.ts';
import {
  createResourceTemplate,
  resourceTemplateErrorMessage,
  resourceTemplateQueryOptions,
  resourceTemplatesQueryKey,
  updateResourceTemplate,
} from '@/queries/resourceTemplates.ts';

export function ResourceTemplateEditor({
  definition,
  onSaved,
  template,
}: {
  definition: FormDefinition;
  onSaved?: (saved: ResourceTemplate) => void | Promise<void>;
  template?: ResourceTemplate;
}) {
  const queryClient = useQueryClient();
  const [baseline, setBaseline] = useState({ definition, template });
  const currentTemplate = baseline.template;
  const defaultsSource = useMemo(
    () => parseResourceDefaultsJson(currentTemplate?.resourceDefaultsJson),
    [currentTemplate?.resourceDefaultsJson]
  );
  const isReadOnly = currentTemplate?.isActive === false;
  const [editorView, setEditorView] = useState<'build' | 'preview' | 'defaults'>('build');
  const activeView = isReadOnly && editorView === 'build' ? 'preview' : editorView;
  const [saved, setSaved] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const saveFlowRef = useRef(false);
  const saveMutation = useMutation({
    mutationFn: (request: SaveResourceTemplateRequest) =>
      currentTemplate
        ? updateResourceTemplate(currentTemplate.id, request)
        : createResourceTemplate(request),
  });
  const form = useForm({
    defaultValues: {
      definition: baseline.definition,
      description: currentTemplate?.description ?? '',
      isActive: currentTemplate?.isActive ?? true,
      name: currentTemplate?.name ?? '',
      resourceDefaults: defaultsSource.defaults,
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
          ...(defaultsSource.unavailableReason ? {} : {
            resourceDefaultsJson: serializeResourceDefaults(value.resourceDefaults, currentTemplate?.resourceDefaultsJson),
          }),
          updatedAt: currentTemplate?.updatedAt,
        });
        setBaseline({ definition: value.definition, template: result });
        form.reset({
          ...value,
          description: result.description ?? '',
          isActive: result.isActive,
          name: result.name,
          resourceDefaults: parseResourceDefaultsJson(result.resourceDefaultsJson).defaults,
        });
        queryClient.setQueryData(resourceTemplateQueryOptions(result.id).queryKey, result);
        await queryClient.invalidateQueries({ queryKey: resourceTemplatesQueryKey });
        setSaved(true);
        await onSaved?.(result);
      } catch {
        // Keep the entered values available when saving fails.
      } finally {
        saveFlowRef.current = false;
        setIsSaving(false);
      }
    },
    validators: {
      onSubmit: ({ value }) => {
        if (!value.name.trim() || value.name.trim().length > 200) {
          return 'Enter a template name of 200 characters or fewer.';
        }
        if (value.description.trim().length > 2000) {
          return 'Enter a description of 2,000 characters or fewer.';
        }
        const defaultsErrors = validateResourceDefaults(value.resourceDefaults);
        if (defaultsErrors.length) {
          return defaultsErrors.join(' ');
        }
        if (value.definition.fields.length === 0) {
          return 'Add at least one form field before saving the template.';
        }
        const errors = validateFormDefinition(value.definition);
        return errors.length ? errors.join(' ') : undefined;
      },
    },
  });
  const blocker = useBlocker({
    enableBeforeUnload: () => form.state.isDirty || saveFlowRef.current,
    shouldBlockFn: () => form.state.isDirty,
    withResolver: true,
  });

  return (
    <main className="content-container py-4 sm:py-8">
      <nav aria-label="Breadcrumb" className="mb-5 text-sm text-base-content/65">
        <Link className="hover:text-primary hover:underline" to="/admin">Site admin</Link>
        {' / '}
        <Link className="hover:text-primary hover:underline" to="/admin/resource-templates">Resource templates</Link>
        {' / '}<span aria-current="page">{isReadOnly ? 'View template' : currentTemplate ? 'Edit template' : 'Create template'}</span>
      </nav>
      <h1 className="text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
        {isReadOnly ? 'View resource template' : currentTemplate ? 'Edit resource template' : 'Create resource template'}
      </h1>
      <p className="mt-3 max-w-2xl text-base-content/70">
        {isReadOnly
          ? 'Review this archived form and its saved version.'
          : 'Build the form people will fill out when requesting a resource. This template is available at the site level.'}
      </p>
      {currentTemplate && (
        <div className="mt-4 space-y-2">
          <span className="badge badge-outline">Version {currentTemplate.formSchemaVersion}</span>
          {isReadOnly ? (
            <div className="space-y-3 rounded-lg border border-base-300 bg-base-200 p-4">
              <p>Archived templates are read-only. Duplicate this template to make changes.</p>
              <Link className="link link-primary" to="/admin/resource-templates">Back to resource templates</Link>
            </div>
          ) : (
            <p className="max-w-2xl text-sm text-base-content/65">
              Saving changes to the form or resource defaults creates a new active version and archives this one. Previous templates keep their original ID and contents. Changing only the name or description, or archiving, keeps the same version.
            </p>
          )}
        </div>
      )}

      {blocker.status === 'blocked' && (
        <section aria-label="Unsaved changes" className="mt-6 rounded-xl border border-warning bg-warning/10 p-5" role="alert">
          <h2 className="font-semibold">Leave this editor?</h2>
          <p className="mt-1">Your unsaved changes will be lost. Wait for any save to finish before leaving.</p>
          <div className="mt-3 flex gap-3">
            <button className="btn btn-primary btn-sm" onClick={() => blocker.reset()} type="button">Keep editing</button>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(isSubmitting) => <button className="btn btn-outline btn-sm" disabled={isSaving || isSubmitting} onClick={() => blocker.proceed()} type="button">Discard changes</button>}
            </form.Subscribe>
          </div>
        </section>
      )}

      <form
        className="mt-8"
        noValidate
        onChange={() => { setSaved(false); saveMutation.reset(); }}
        onSubmit={(event) => {
          event.preventDefault();
          if (!isReadOnly && !saveFlowRef.current && !form.state.isSubmitting) {
            if (validateResourceDefaults(form.state.values.resourceDefaults).length) {
              setEditorView('defaults');
            }
            void form.handleSubmit();
          }
        }}
      >
        <form.Subscribe selector={(state) => state.isSubmitting}>
          {(isSubmitting) => (
            <fieldset className="space-y-6" disabled={isSaving || isSubmitting}>
              <fieldset className="space-y-5 rounded-xl border border-base-300 bg-base-100 p-5" disabled={isReadOnly}>
                <div className="flex flex-col gap-5 sm:flex-row sm:items-end">
                  <form.Field name="name">
                    {(field) => (
                      <div className="grow">
                        <label className="mb-2 block text-sm font-semibold" htmlFor="template-name">Template name</label>
                        <input className="input input-bordered w-full" id="template-name" maxLength={200} onBlur={field.handleBlur} onChange={(event) => field.handleChange(event.target.value)} required value={field.state.value} />
                      </div>
                    )}
                  </form.Field>
                  <form.Field name="isActive">
                    {(field) => (
                      <label className="flex items-center gap-3 py-3">
                        <input checked={field.state.value} className="checkbox checkbox-primary" onChange={(event) => field.handleChange(event.target.checked)} type="checkbox" />
                        Active template
                      </label>
                    )}
                  </form.Field>
                  {!isReadOnly && (
                    <button className="btn btn-primary" type="submit">
                      {isSaving || isSubmitting ? 'Saving…' : currentTemplate ? 'Save template' : 'Create template'}
                    </button>
                  )}
                </div>
                <form.Field name="description">
                  {(field) => (
                    <div>
                      <label className="mb-2 block text-sm font-semibold" htmlFor="template-description">Description</label>
                      <textarea
                        aria-describedby="template-description-help"
                        className="textarea textarea-bordered w-full"
                        id="template-description"
                        maxLength={2000}
                        onBlur={field.handleBlur}
                        onChange={(event) => field.handleChange(event.target.value)}
                        rows={3}
                        value={field.state.value}
                      />
                      <p className="mt-2 text-sm text-base-content/65" id="template-description-help">Optional. Describe what this template is for in 2,000 characters or fewer.</p>
                    </div>
                  )}
                </form.Field>
              </fieldset>
              {!isReadOnly && (
                <p className="text-sm text-base-content/65">Clear Active template and save to archive it. Changes to the form or resource defaults always create an active version; save that version before archiving.</p>
              )}
              <div aria-label="Editor view" className="flex flex-wrap gap-2" role="group">
                {!isReadOnly && <button aria-pressed={activeView === 'build'} className={`btn btn-sm ${activeView === 'build' ? 'btn-primary' : 'btn-ghost'}`} onClick={() => setEditorView('build')} type="button">Build form</button>}
                <button aria-pressed={activeView === 'preview'} className={`btn btn-sm ${activeView === 'preview' ? 'btn-primary' : 'btn-ghost'}`} onClick={() => setEditorView('preview')} type="button">Preview form</button>
                <button aria-pressed={activeView === 'defaults'} className={`btn btn-sm ${activeView === 'defaults' ? 'btn-primary' : 'btn-ghost'}`} onClick={() => setEditorView('defaults')} type="button">Resource defaults</button>
              </div>
              {activeView === 'build' && (
                <form.Field name="definition">
                  {(field) => <FormBuilder disabled={isSaving || isSubmitting} onChange={(value) => { field.handleChange(value); setSaved(false); saveMutation.reset(); }} value={field.state.value} />}
                </form.Field>
              )}
              {activeView === 'defaults' && (
                <form.Field name="resourceDefaults" validators={{ onChange: ({ value }) => validateResourceDefaults(value).join(' ') || undefined }}>
                  {(field) => (
                    <ResourceDefaultsEditor
                      disabled={isSaving || isSubmitting}
                      errors={field.state.meta.errors.map(String)}
                      onChange={(value) => { field.handleChange(value); setSaved(false); saveMutation.reset(); }}
                      readOnly={isReadOnly}
                      unavailableReason={defaultsSource.unavailableReason}
                      value={field.state.value}
                    />
                  )}
                </form.Field>
              )}
            </fieldset>
          )}
        </form.Subscribe>
        <form.Subscribe selector={(state) => [state.errors, state.isDirty] as const}>
          {([errors, isDirty]) => (
            <div aria-live="polite" className="mt-5">
              {errors.length > 0 && <p className="text-error" role="alert">{errors.join(' ')}</p>}
              {saveMutation.isError && <p className="text-error" role="alert">{resourceTemplateErrorMessage(saveMutation.error)}</p>}
              {saved && !isDirty && <p className="text-success" role="status">Template saved.</p>}
              {isDirty && <p className="mt-2 text-sm text-base-content/65">You have unsaved changes.</p>}
            </div>
          )}
        </form.Subscribe>
      </form>
      {activeView === 'preview' && (
        <form.Subscribe selector={(state) => state.values.definition}>
          {(currentDefinition) => <div className="mt-6"><FormPreview definition={currentDefinition} /></div>}
        </form.Subscribe>
      )}
    </main>
  );
}
