import { useMutation } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import type { TeamPaymentsSettings } from './models/TeamPaymentsSettings.ts';
import { HttpError } from '@/lib/api.ts';
import { saveTeamPaymentsSettings } from '@/queries/teams.ts';
import { useAppForm } from '@/shared/forms/formContext.tsx';

export function EditTeamPaymentsDialog({
  isReplacing,
  onAccessDenied,
  onClose,
  onSaved,
  teamSlug,
}: {
  isReplacing: boolean;
  onAccessDenied: () => void;
  onClose: () => void;
  onSaved: (settings: TeamPaymentsSettings) => void | Promise<void>;
  teamSlug: string;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const requestRef = useRef<AbortController | null>(null);
  const activeRef = useRef(true);
  const [errorMessage, setErrorMessage] = useState('');
  const saveMutation = useMutation<TeamPaymentsSettings, Error, void>({
    gcTime: 0,
    // Read the candidate from the form so it never enters mutation variables.
    mutationFn: () =>
      saveTeamPaymentsSettings(
        teamSlug,
        form.getFieldValue('apiKey'),
        requestRef.current?.signal
      ),
    retry: false,
  });
  const { reset: resetMutation } = saveMutation;
  const form = useAppForm({
    defaultValues: { apiKey: '' },
    onSubmit: async (): Promise<void> => {
      if (saveMutation.isPending) {
        return;
      }
      setErrorMessage('');
      requestRef.current = new AbortController();
      try {
        const settings = await saveMutation.mutateAsync();
        if (activeRef.current) {
          form.reset();
          await onSaved(settings);
        }
      } catch (error) {
        if (!activeRef.current) {
          return;
        }
        if (error instanceof HttpError && error.status === 403) {
          form.reset();
          onAccessDenied();
        } else {
          setErrorMessage(
            error instanceof HttpError && error.status === 400
              ? 'This API key could not be validated. Check the key and try again. Your saved payments settings have not changed.'
              : 'We could not confirm the save. Check the connection before trying again.'
          );
        }
      } finally {
        resetMutation();
      }
    },
  });

  useEffect(() => {
    activeRef.current = true;
    const dialog = dialogRef.current;
    const previousFocus = document.activeElement as HTMLElement | null;
    dialog?.showModal();

    return () => {
      activeRef.current = false;
      requestRef.current?.abort();
      resetMutation();
      dialog?.close();
      if (previousFocus?.isConnected) {
        previousFocus.focus?.();
      }
    };
  }, [resetMutation]);

  function close() {
    form.reset();
    resetMutation();
    onClose();
  }

  return (
    <dialog
      aria-describedby="edit-team-payments-description"
      aria-labelledby="edit-team-payments-title"
      className="modal"
      onCancel={(event) => {
        event.preventDefault();
        if (!saveMutation.isPending) {
          close();
        }
      }}
      ref={dialogRef}
    >
      <div className="modal-box max-w-xl">
        <h2
          className="text-2xl font-semibold text-primary"
          id="edit-team-payments-title"
        >
          {isReplacing ? 'Replace payments API key' : 'Set payments API key'}
        </h2>
        <p
          className="mt-2 text-base-content/70"
          id="edit-team-payments-description"
        >
          Enter your team&apos;s Payments API key. We will verify it before
          saving and use it to identify your Payments team.
        </p>
        <form
          className="mt-6"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            event.stopPropagation();
            void form.handleSubmit();
          }}
        >
          <form.AppField
            name="apiKey"
            validators={{
              onBlur: ({ value }) =>
                value.trim() ? undefined : 'Enter a Payments API key.',
              onSubmit: ({ value }) =>
                value.trim() ? undefined : 'Enter a Payments API key.',
            }}
          >
            {(field) => {
              const hasError =
                field.state.meta.isTouched && !field.state.meta.isValid;
              return (
                <div>
                  <label
                    className="mb-2 block text-sm font-semibold"
                    htmlFor="team-payments-api-key"
                  >
                    Payments API key
                  </label>
                  <input
                    aria-describedby={
                      hasError ? 'team-payments-api-key-error' : undefined
                    }
                    aria-invalid={hasError || undefined}
                    autoCapitalize="none"
                    autoComplete="new-password"
                    className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                    disabled={saveMutation.isPending}
                    id="team-payments-api-key"
                    maxLength={4096}
                    name={field.name}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      field.handleChange(event.target.value);
                      setErrorMessage('');
                    }}
                    required
                    spellCheck={false}
                    type="password"
                    value={field.state.value}
                  />
                  {hasError && (
                    <p
                      className="mt-2 text-sm text-error"
                      id="team-payments-api-key-error"
                      role="alert"
                    >
                      {field.state.meta.errors.join(' ')}
                    </p>
                  )}
                </div>
              );
            }}
          </form.AppField>
          {errorMessage && (
            <p className="alert alert-error mt-5" role="alert">
              {errorMessage}
            </p>
          )}
          <div className="modal-action">
            <button
              className="btn btn-ghost"
              disabled={saveMutation.isPending}
              onClick={close}
              type="button"
            >
              Cancel
            </button>
            <form.Subscribe
              selector={(state) =>
                [
                  state.canSubmit,
                  state.isSubmitting,
                  state.values.apiKey,
                ] as const
              }
            >
              {([canSubmit, isSubmitting, apiKey]) => (
                <button
                  className="btn btn-primary"
                  disabled={
                    !canSubmit ||
                    isSubmitting ||
                    saveMutation.isPending ||
                    !apiKey.trim()
                  }
                  type="submit"
                >
                  {saveMutation.isPending
                    ? 'Verifying and saving…'
                    : 'Verify and save'}
                </button>
              )}
            </form.Subscribe>
          </div>
        </form>
      </div>
    </dialog>
  );
}
