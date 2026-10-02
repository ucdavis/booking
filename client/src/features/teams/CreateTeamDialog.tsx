import { UserGroupIcon } from '@heroicons/react/24/outline';
import { useForm } from '@tanstack/react-form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import type { CreateTeamRequest } from './models/CreateTeamRequest.ts';
import type { TeamSummary } from './models/TeamSummary.ts';
import { HttpError } from '@/lib/api.ts';
import { adminTeamsQueryKey, createTeam } from '@/queries/admin.ts';

function suggestSlug(name: string) {
  return name
    .normalize('NFKD')
    .replaceAll(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replaceAll(/[^\da-z]+/g, '-')
    .replaceAll(/^-+|-+$/g, '')
    .slice(0, 100)
    .replaceAll(/-+$/g, '');
}

export function CreateTeamDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (team: TeamSummary) => void | Promise<void>;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const slugEditedRef = useRef(false);
  const queryClient = useQueryClient();
  const createMutation = useMutation({
    mutationFn: createTeam,
    onSuccess: async (team) => {
      await queryClient.invalidateQueries({ queryKey: adminTeamsQueryKey });
      await onCreated(team);
    },
  });
  const form = useForm({
    defaultValues: { name: '', slug: '' } satisfies CreateTeamRequest,
    onSubmit: async ({ value }) => {
      try {
        await createMutation.mutateAsync({
          name: value.name.trim(),
          slug: value.slug,
        });
      } catch {
        // The mutation error is displayed below; retain the entered values.
      }
    },
  });

  useEffect(() => {
    const dialog = dialogRef.current;
    const previousFocus = document.activeElement as HTMLElement | null;
    dialog?.showModal();

    return () => {
      dialog?.close();
      if (previousFocus?.isConnected) {
        previousFocus.focus?.();
      }
    };
  }, []);

  let errorMessage = 'We could not create the team. Please try again.';
  if (createMutation.error instanceof HttpError) {
    if (createMutation.error.status === 409) {
      errorMessage = 'This URL slug is already in use. Choose another slug.';
    } else if (createMutation.error.status === 403) {
      errorMessage = 'You no longer have permission to create teams.';
    } else if (createMutation.error.status === 400) {
      errorMessage = 'Check the team name and URL slug, then try again.';
    }
  }

  return (
    <dialog
      aria-describedby="create-team-description"
      aria-labelledby="create-team-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!createMutation.isPending) {
          onClose();
        }
      }}
      ref={dialogRef}
    >
      <div className="modal-box max-w-2xl">
        <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <UserGroupIcon aria-hidden="true" className="h-6 w-6" />
        </div>
        <h2
          className="text-2xl font-semibold text-primary"
          id="create-team-title"
        >
          Create a team
        </h2>
        <p className="mt-2 text-base-content/70" id="create-team-description">
          Choose a name and URL for the new team.
        </p>
        <p className="mt-5 rounded-lg border border-warning/40 bg-warning/10 p-4 text-sm">
          The team name and URL slug cannot be changed after creation. Check
          both carefully before creating the team.
        </p>

        <form
          className="mt-6"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            if (!createMutation.isPending) {
              void form.handleSubmit();
            }
          }}
        >
          <div className="space-y-5">
            <form.Field
              name="name"
              validators={{
                onChange: ({ value }) =>
                  !value.trim()
                    ? 'Enter a team name.'
                    : value.trim().length > 200
                      ? 'Use 200 characters or fewer.'
                      : undefined,
              }}
            >
              {(field) => {
                const hasError =
                  field.state.meta.isTouched && !field.state.meta.isValid;
                return (
                  <div>
                    <label
                      className="mb-2 block text-sm font-semibold"
                      htmlFor="create-team-name"
                    >
                      Team name
                    </label>
                    <input
                      aria-describedby={
                        hasError ? 'create-team-name-error' : undefined
                      }
                      aria-invalid={hasError || undefined}
                      className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                      disabled={createMutation.isPending}
                      id="create-team-name"
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
                        createMutation.reset();
                      }}
                      required
                      type="text"
                      value={field.state.value}
                    />
                    {hasError && (
                      <p
                        className="mt-2 text-sm text-error"
                        id="create-team-name-error"
                        role="alert"
                      >
                        {field.state.meta.errors.join(' ')}
                      </p>
                    )}
                  </div>
                );
              }}
            </form.Field>

            <form.Field
              name="slug"
              validators={{
                onChange: ({ value }) =>
                  !value
                    ? 'Enter a URL slug.'
                    : value.length > 100
                      ? 'Use 100 characters or fewer.'
                      : !/^[\da-z]+(?:-[\da-z]+)*$/.test(value)
                        ? 'Use lowercase letters, numbers, and single hyphens between words.'
                        : undefined,
              }}
            >
              {(field) => {
                const hasError =
                  field.state.meta.isTouched && !field.state.meta.isValid;
                return (
                  <div>
                    <label
                      className="mb-2 block text-sm font-semibold"
                      htmlFor="create-team-slug"
                    >
                      URL slug
                    </label>
                    <input
                      aria-describedby={`create-team-slug-help${hasError ? ' create-team-slug-error' : ''}`}
                      aria-invalid={hasError || undefined}
                      autoCapitalize="none"
                      autoComplete="off"
                      className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                      disabled={createMutation.isPending}
                      id="create-team-slug"
                      maxLength={100}
                      name={field.name}
                      onBlur={field.handleBlur}
                      onChange={(event) => {
                        slugEditedRef.current = true;
                        field.handleChange(event.target.value);
                        createMutation.reset();
                      }}
                      required
                      spellCheck={false}
                      type="text"
                      value={field.state.value}
                    />
                    <p
                      className="mt-2 text-sm text-base-content/65"
                      id="create-team-slug-help"
                    >
                      Use lowercase letters, numbers, and hyphens. Team URL:{' '}
                      <span className="break-all font-medium">
                        /teams/{field.state.value || 'your-team'}
                      </span>
                    </p>
                    {hasError && (
                      <p
                        className="mt-2 text-sm text-error"
                        id="create-team-slug-error"
                        role="alert"
                      >
                        {field.state.meta.errors.join(' ')}
                      </p>
                    )}
                  </div>
                );
              }}
            </form.Field>
          </div>

          {createMutation.isError && (
            <p className="alert alert-error mt-5" role="alert">
              {errorMessage}
            </p>
          )}

          <div className="modal-action">
            <button
              className="btn btn-ghost"
              disabled={createMutation.isPending}
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
                    createMutation.isPending ||
                    !name.trim() ||
                    !slug
                  }
                  type="submit"
                >
                  {createMutation.isPending ? 'Creating…' : 'Create team'}
                </button>
              )}
            </form.Subscribe>
          </div>
        </form>
      </div>
    </dialog>
  );
}
