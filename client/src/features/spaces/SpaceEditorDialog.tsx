import { BuildingOfficeIcon } from '@heroicons/react/24/outline';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import type { SaveSpaceRequest } from './models/SaveSpaceRequest.ts';
import type { Space } from './models/Space.ts';
import { HttpError } from '@/lib/api.ts';
import {
  adminSpacesQueryKey,
  createSpace,
  updateSpace,
} from '@/queries/admin-spaces.ts';
import { FieldWrapper } from '@/shared/forms/fieldWrapper.tsx';
import { useAppForm } from '@/shared/forms/formContext.tsx';

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

function readErrorCode(body: unknown): string | undefined {
  let details = body;
  if (typeof details === 'string') {
    try {
      details = JSON.parse(details);
    } catch {
      return undefined;
    }
  }

  if (
    details !== null &&
    typeof details === 'object' &&
    'code' in details &&
    typeof details.code === 'string'
  ) {
    return details.code;
  }

  return undefined;
}

export function SpaceEditorDialog({
  onClose,
  onSaved,
  space,
}: {
  onClose: () => void;
  onSaved: (space: Space) => void;
  space: Space | null;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const nameInputRef = useRef<HTMLInputElement>(null);
  const slugEditedRef = useRef(space !== null);
  const queryClient = useQueryClient();
  const saveMutation = useMutation({
    mutationFn: (request: SaveSpaceRequest) =>
      space ? updateSpace(space.id, request) : createSpace(request),
    onSuccess: async (savedSpace) => {
      await queryClient.invalidateQueries({ queryKey: adminSpacesQueryKey });
      onSaved(savedSpace);
    },
  });
  const form = useAppForm({
    defaultValues: {
      address: space?.address ?? '',
      description: space?.description ?? '',
      isActive: space?.isActive ?? true,
      isOfficialFacility: space?.isOfficialFacility ?? false,
      name: space?.name ?? '',
      referenceNumber: space?.referenceNumber ?? '',
      slug: space?.slug ?? '',
    } satisfies SaveSpaceRequest,
    onSubmit: async ({ value }) => {
      try {
        await saveMutation.mutateAsync({
          address: value.address.trim() || null,
          description: value.description.trim() || null,
          isActive: value.isActive,
          isOfficialFacility: value.isOfficialFacility,
          name: value.name.trim(),
          referenceNumber: value.referenceNumber.trim() || null,
          slug: space?.slug ?? value.slug,
        });
      } catch {
        // Keep the entered values while the mutation error is displayed.
      }
    },
  });

  useEffect(() => {
    const dialog = dialogRef.current;
    const previousFocus = document.activeElement as HTMLElement | null;
    dialog?.showModal();
    nameInputRef.current?.focus();

    return () => {
      dialog?.close();
      if (previousFocus?.isConnected) {
        previousFocus.focus?.();
      }
    };
  }, []);

  let errorMessage = 'We could not save the space. Please try again.';
  if (saveMutation.error instanceof HttpError) {
    if (saveMutation.error.status === 409) {
      errorMessage =
        readErrorCode(saveMutation.error.body) === 'duplicate_reference_number'
          ? 'This reference number is already in use by another official facility.'
          : 'This URL slug is already in use. Choose another slug.';
    } else if (saveMutation.error.status === 403) {
      errorMessage = 'You no longer have permission to manage spaces.';
    } else if (saveMutation.error.status === 400) {
      errorMessage = 'Check the space details, then try again.';
    } else if (saveMutation.error.status === 404) {
      errorMessage =
        'This space could not be found. Close this dialog and refresh the list.';
    }
  }

  return (
    <dialog
      aria-describedby="space-editor-description"
      aria-labelledby="space-editor-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!saveMutation.isPending) {
          onClose();
        }
      }}
      ref={dialogRef}
    >
      <div className="modal-box max-w-2xl">
        <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <BuildingOfficeIcon aria-hidden="true" className="h-6 w-6" />
        </div>
        <h2
          className="text-2xl font-semibold text-primary"
          id="space-editor-title"
        >
          {space ? 'Edit space' : 'Create a space'}
        </h2>
        <p className="mt-2 text-base-content/70" id="space-editor-description">
          A space can be a building or any named location.
        </p>

        <form
          className="mt-6"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            if (!saveMutation.isPending) {
              void form.handleSubmit();
            }
          }}
        >
          <fieldset className="space-y-5" disabled={saveMutation.isPending}>
            <form.AppField
              name="name"
              validators={{
                onChange: ({ value }) =>
                  !value.trim()
                    ? 'Enter a space name.'
                    : value.trim().length > 200
                      ? 'Use 200 characters or fewer.'
                      : undefined,
              }}
            >
              {(field) => {
                const hasError =
                  field.state.meta.isTouched && !field.state.meta.isValid;
                return (
                  <FieldWrapper controlId="space-name" label="Space name">
                    <input
                      aria-describedby={
                        hasError ? 'space-name-error' : undefined
                      }
                      aria-invalid={hasError || undefined}
                      className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                      id="space-name"
                      maxLength={200}
                      name={field.name}
                      onBlur={field.handleBlur}
                      onChange={(event) => {
                        field.handleChange(event.target.value);
                        if (!slugEditedRef.current) {
                          form.setFieldValue(
                            'slug',
                            suggestSlug(event.target.value)
                          );
                        }
                        saveMutation.reset();
                      }}
                      ref={nameInputRef}
                      required
                      type="text"
                      value={field.state.value}
                    />
                  </FieldWrapper>
                );
              }}
            </form.AppField>
            <form.AppField
              name="slug"
              validators={{
                onChange: ({ value }) =>
                  !value
                    ? 'Enter a URL slug.'
                    : value.length > 120
                      ? 'Use 120 characters or fewer.'
                      : !/^[\da-z]+(?:-[\da-z]+)*$/.test(value)
                        ? 'Use lowercase letters, numbers, and single hyphens between words.'
                        : undefined,
              }}
            >
              {(field) => {
                const hasError =
                  field.state.meta.isTouched && !field.state.meta.isValid;
                return (
                  <FieldWrapper controlId="space-slug" label="URL slug">
                    <input
                      aria-describedby={`space-slug-help${hasError ? ' space-slug-error' : ''}`}
                      aria-invalid={hasError || undefined}
                      autoCapitalize="none"
                      autoComplete="off"
                      className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                      id="space-slug"
                      maxLength={120}
                      name={field.name}
                      onBlur={field.handleBlur}
                      onChange={(event) => {
                        if (space) {
                          return;
                        }
                        slugEditedRef.current = true;
                        field.handleChange(event.target.value);
                        saveMutation.reset();
                      }}
                      readOnly={space !== null}
                      required
                      spellCheck={false}
                      type="text"
                      value={field.state.value}
                    />
                    <p
                      className="mt-2 text-sm text-base-content/65"
                      id="space-slug-help"
                    >
                      {space
                        ? 'The URL slug cannot be changed after creation.'
                        : 'Use lowercase letters, numbers, and hyphens. You can change the suggested slug.'}
                    </p>
                  </FieldWrapper>
                );
              }}
            </form.AppField>

            <form.AppField name="description">
              {(field) => (
                <FieldWrapper controlId="space-description" label="Description">
                  <textarea
                    className="textarea textarea-bordered w-full"
                    id="space-description"
                    name={field.name}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      field.handleChange(event.target.value);
                      saveMutation.reset();
                    }}
                    rows={4}
                    value={field.state.value}
                  />
                </FieldWrapper>
              )}
            </form.AppField>

            <form.AppField
              name="address"
              validators={{
                onChange: ({ value }) =>
                  value.trim().length > 500
                    ? 'Use 500 characters or fewer.'
                    : undefined,
              }}
            >
              {(field) => {
                const hasError =
                  field.state.meta.isTouched && !field.state.meta.isValid;
                return (
                  <FieldWrapper controlId="space-address" label="Address">
                    <input
                      aria-describedby={
                        hasError ? 'space-address-error' : undefined
                      }
                      aria-invalid={hasError || undefined}
                      className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                      id="space-address"
                      maxLength={500}
                      name={field.name}
                      onBlur={field.handleBlur}
                      onChange={(event) => {
                        field.handleChange(event.target.value);
                        saveMutation.reset();
                      }}
                      type="text"
                      value={field.state.value}
                    />
                  </FieldWrapper>
                );
              }}
            </form.AppField>

            <form.Field name="isOfficialFacility">
              {(field) => (
                <label
                  className="flex items-center gap-3 font-medium"
                  htmlFor="space-official-facility"
                >
                  <input
                    checked={field.state.value}
                    className="checkbox checkbox-primary"
                    id="space-official-facility"
                    name={field.name}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      field.handleChange(event.target.checked);
                      if (event.target.checked) {
                        form.setFieldMeta('referenceNumber', (meta) => ({
                          ...meta,
                          isTouched: true,
                        }));
                      }
                      saveMutation.reset();
                    }}
                    type="checkbox"
                  />
                  Official facility
                </label>
              )}
            </form.Field>

            <form.Subscribe
              selector={(state) => state.values.isOfficialFacility}
            >
              {(isOfficialFacility) => (
                <form.AppField
                  name="referenceNumber"
                  validators={{
                    onChange: ({ fieldApi, value }) =>
                      fieldApi.form.getFieldValue('isOfficialFacility') &&
                      !value.trim()
                        ? 'Enter a reference number for an official facility.'
                        : value.trim().length > 100
                          ? 'Use 100 characters or fewer.'
                          : undefined,
                    onChangeListenTo: ['isOfficialFacility'],
                  }}
                >
                  {(field) => {
                    const hasError =
                      field.state.meta.isTouched && !field.state.meta.isValid;
                    return (
                      <FieldWrapper
                        controlId="space-reference-number"
                        label="Reference number"
                      >
                        <input
                          aria-describedby={`space-reference-number-help${hasError ? ' space-reference-number-error' : ''}`}
                          aria-invalid={hasError || undefined}
                          className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                          id="space-reference-number"
                          maxLength={100}
                          name={field.name}
                          onBlur={field.handleBlur}
                          onChange={(event) => {
                            field.handleChange(event.target.value);
                            saveMutation.reset();
                          }}
                          required={isOfficialFacility}
                          type="text"
                          value={field.state.value}
                        />
                        <p
                          className="mt-2 text-sm text-base-content/65"
                          id="space-reference-number-help"
                        >
                          Required for official facilities.
                        </p>
                      </FieldWrapper>
                    );
                  }}
                </form.AppField>
              )}
            </form.Subscribe>

            <form.Field name="isActive">
              {(field) => (
                <label
                  className="flex items-center gap-3 font-medium"
                  htmlFor="space-active"
                >
                  <input
                    checked={field.state.value}
                    className="checkbox checkbox-primary"
                    id="space-active"
                    name={field.name}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      field.handleChange(event.target.checked);
                      saveMutation.reset();
                    }}
                    type="checkbox"
                  />
                  Active
                </label>
              )}
            </form.Field>
          </fieldset>

          {saveMutation.isError && (
            <p className="alert alert-error mt-5" role="alert">
              {errorMessage}
            </p>
          )}

          <div className="modal-action">
            <button
              className="btn btn-ghost"
              disabled={saveMutation.isPending}
              onClick={onClose}
              type="button"
            >
              Cancel
            </button>
            <form.Subscribe
              selector={(state) =>
                [
                  state.canSubmit,
                  state.isSubmitting,
                  state.values.name,
                  state.values.slug,
                ] as const
              }
            >
              {([canSubmit, isSubmitting, name, slug]) => (
                <button
                  className="btn btn-primary"
                  disabled={
                    !canSubmit ||
                    isSubmitting ||
                    saveMutation.isPending ||
                    !name.trim() ||
                    !slug
                  }
                  type="submit"
                >
                  {saveMutation.isPending
                    ? 'Saving…'
                    : space
                      ? 'Save changes'
                      : 'Create space'}
                </button>
              )}
            </form.Subscribe>
          </div>
        </form>
      </div>
    </dialog>
  );
}
