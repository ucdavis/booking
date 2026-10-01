import { useForm } from '@tanstack/react-form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useBlocker } from '@tanstack/react-router';
import { useState } from 'react';
import { FormBuilder } from './FormBuilder.tsx';
import { FormPreview } from './FormPreview.tsx';
import { validateFormDefinition } from './formDefinition.ts';
import type { FormDefinition } from './models/FormDefinition.ts';
import type { ResourceTemplate } from './models/ResourceTemplate.ts';
import type { SaveResourceTemplateRequest } from './models/SaveResourceTemplateRequest.ts';
import {
  createResourceTemplate,
  resourceTemplateErrorMessage,
  resourceTemplateQueryOptions,
  resourceTemplatesQueryKey,
  updateResourceTemplate,
} from '@/queries/resourceTemplates.ts';

export function ResourceTemplateEditor({
  template,
  definition,
  onSaved,
}: {
  template?: ResourceTemplate;
  definition: FormDefinition;
  onSaved?: (saved: ResourceTemplate) => void | Promise<void>;
}) {
  const queryClient = useQueryClient();
  const [showPreview, setShowPreview] = useState(false);
  const [saved, setSaved] = useState(false);
  const saveMutation = useMutation({
    mutationFn: (request: SaveResourceTemplateRequest) =>
      template
        ? updateResourceTemplate(template.id, request)
        : createResourceTemplate(request),
  });
  const form = useForm({
    defaultValues: {
      definition,
      isActive: template?.isActive ?? true,
      name: template?.name ?? '',
    },
    onSubmit: async ({ value }) => {
      setSaved(false);
      try {
        const result = await saveMutation.mutateAsync({
          formJson: JSON.stringify(value.definition),
          formSchemaVersion: 1,
          isActive: value.isActive,
          name: value.name.trim(),
        });
        form.reset({ ...value, name: result.name });
        queryClient.setQueryData(resourceTemplateQueryOptions(result.id).queryKey, result);
        await queryClient.invalidateQueries({ queryKey: resourceTemplatesQueryKey });
        setSaved(true);
        await onSaved?.(result);
      } catch {
        // Keep the entered values available when saving fails.
      }
    },
    validators: {
      onSubmit: ({ value }) => {
        if (!value.name.trim() || value.name.trim().length > 200) {
          return 'Enter a template name of 200 characters or fewer.';
        }
        const errors = validateFormDefinition(value.definition);
        return errors.length ? errors.join(' ') : undefined;
      },
    },
  });
  const blocker = useBlocker({
    enableBeforeUnload: () => form.state.isDirty || form.state.isSubmitting,
    shouldBlockFn: () => form.state.isDirty,
    withResolver: true,
  });

  return (
    <main className="content-container py-4 sm:py-8">
      <nav aria-label="Breadcrumb" className="mb-5 text-sm text-base-content/65">
        <Link className="hover:text-primary hover:underline" to="/admin">Site admin</Link>
        {' / '}
        <Link className="hover:text-primary hover:underline" to="/admin/resource-templates">Resource templates</Link>
        {' / '}<span aria-current="page">{template ? 'Edit template' : 'Create template'}</span>
      </nav>
      <h1 className="text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
        {template ? 'Edit resource template' : 'Create resource template'}
      </h1>
      <p className="mt-3 max-w-2xl text-base-content/70">
        Build the form people will fill out when requesting a resource. This template is available at the site level.
      </p>

      {blocker.status === 'blocked' && (
        <section aria-label="Unsaved changes" className="mt-6 rounded-xl border border-warning bg-warning/10 p-5" role="alert">
          <h2 className="font-semibold">Leave this editor?</h2>
          <p className="mt-1">Your unsaved changes will be lost. Wait for any save to finish before leaving.</p>
          <div className="mt-3 flex gap-3">
            <button className="btn btn-primary btn-sm" onClick={() => blocker.reset()} type="button">Keep editing</button>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(isSubmitting) => <button className="btn btn-outline btn-sm" disabled={isSubmitting} onClick={() => blocker.proceed()} type="button">Discard changes</button>}
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
          if (!form.state.isSubmitting) void form.handleSubmit();
        }}
      >
        <form.Subscribe selector={(state) => state.isSubmitting}>
          {(isSubmitting) => (
            <fieldset className="space-y-6" disabled={isSubmitting}>
              <div className="flex flex-col gap-5 rounded-xl border border-base-300 bg-base-100 p-5 sm:flex-row sm:items-end">
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
                <button className="btn btn-primary" type="submit">
                  {isSubmitting ? 'Saving…' : template ? 'Save template' : 'Create template'}
                </button>
              </div>
              <p className="text-sm text-base-content/65">Clear Active template and save to archive it. Archived templates can be restored here.</p>
              <div aria-label="Editor view" className="flex gap-2">
                <button aria-pressed={!showPreview} className={`btn btn-sm ${!showPreview ? 'btn-primary' : 'btn-ghost'}`} onClick={() => setShowPreview(false)} type="button">Build form</button>
                <button aria-pressed={showPreview} className={`btn btn-sm ${showPreview ? 'btn-primary' : 'btn-ghost'}`} onClick={() => setShowPreview(true)} type="button">Preview form</button>
              </div>
              {!showPreview && (
                <form.Field name="definition">
                  {(field) => <FormBuilder disabled={isSubmitting} onChange={(value) => { field.handleChange(value); setSaved(false); saveMutation.reset(); }} value={field.state.value} />}
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
      {showPreview && (
        <form.Subscribe selector={(state) => state.values.definition}>
          {(currentDefinition) => <div className="mt-6"><FormPreview definition={currentDefinition} /></div>}
        </form.Subscribe>
      )}
    </main>
  );
}
