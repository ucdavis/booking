import { useId } from 'react';
import { fieldTypeLabels } from './formDefinition.ts';
import type { FormField } from './models/FormField.ts';
import type { FormValidation } from './models/FormValidation.ts';

export function FormFieldEditor({
  disabled = false,
  field,
  onChange,
}: {
  disabled?: boolean;
  field: FormField;
  onChange: (field: FormField) => void;
}) {
  const prefix = useId();
  const isChoice = ['checkboxes', 'dropdown', 'radio'].includes(field.type);
  const isTextInput = field.type === 'input' || field.type === 'textarea';
  const updateValidation = (change: Partial<FormValidation>) =>
    onChange({ ...field, validation: { ...field.validation, ...change } });

  return (
    <fieldset className="min-w-0 space-y-5" disabled={disabled}>
      <legend className="mb-4 text-lg font-semibold text-primary">
        {fieldTypeLabels[field.type]} settings
      </legend>
      <div>
        <label
          className="mb-2 block text-sm font-semibold"
          htmlFor={`${prefix}-label`}
        >
          {field.type === 'text' ? 'Text content' : 'Label'}
        </label>
        <textarea
          className="textarea textarea-bordered w-full"
          id={`${prefix}-label`}
          maxLength={10_000}
          onChange={(event) =>
            onChange({ ...field, label: event.target.value })
          }
          rows={field.type === 'text' ? 5 : 2}
          value={field.label}
        />
      </div>
      <div>
        <label
          className="mb-2 block text-sm font-semibold"
          htmlFor={`${prefix}-help`}
        >
          Help text
        </label>
        <textarea
          className="textarea textarea-bordered w-full"
          id={`${prefix}-help`}
          maxLength={2000}
          onChange={(event) =>
            onChange({ ...field, helpText: event.target.value })
          }
          rows={2}
          value={field.helpText ?? ''}
        />
      </div>
      {field.type !== 'text' && (
        <label className="flex items-center gap-3 text-sm font-semibold">
          <input
            checked={field.validation?.required ?? false}
            className="checkbox checkbox-primary"
            onChange={(event) =>
              updateValidation({ required: event.target.checked })
            }
            type="checkbox"
          />
          Required
        </label>
      )}
      {isTextInput && (
        <div>
          <div className="grid gap-4 sm:grid-cols-2">
            {(['minLength', 'maxLength'] as const).map((rule) => (
              <div key={rule}>
                <label
                  className="mb-2 block text-sm font-semibold"
                  htmlFor={`${prefix}-${rule}`}
                >
                  {rule === 'minLength' ? 'Minimum length' : 'Maximum length'}
                </label>
                <input
                  aria-describedby={`${prefix}-length-help`}
                  className="input input-bordered w-full"
                  id={`${prefix}-${rule}`}
                  max={100_000}
                  min={1}
                  onChange={(event) => {
                    const length = event.target.valueAsNumber;
                    if (event.target.value === '' || Number.isFinite(length)) {
                      updateValidation({
                        [rule]: event.target.value === '' ? undefined : length,
                      });
                    }
                  }}
                  step={1}
                  type="number"
                  value={field.validation?.[rule] ?? ''}
                />
              </div>
            ))}
          </div>
          <p
            className="mt-2 text-sm text-base-content/65"
            id={`${prefix}-length-help`}
          >
            Leave blank for no limit. Length limits apply when an answer is
            entered.
          </p>
          {field.validation?.minLength !== undefined &&
            field.validation.maxLength !== undefined &&
            field.validation.minLength > field.validation.maxLength && (
              <p className="mt-2 text-sm text-error" role="alert">
                Minimum length cannot exceed maximum length.
              </p>
            )}
        </div>
      )}
      {isChoice && (
        <fieldset className="space-y-3">
          <legend className="mb-2 text-sm font-semibold">Choices</legend>
          {(field.options ?? []).map((option, index) => (
            <div key={option.id}>
              <label
                className="mb-1 block text-sm"
                htmlFor={`${prefix}-${option.id}`}
              >
                Choice {index + 1}
              </label>
              <div className="flex items-start gap-2">
                <input
                  className="input input-bordered min-w-0 flex-1"
                  id={`${prefix}-${option.id}`}
                  maxLength={200}
                  onChange={(event) =>
                    onChange({
                      ...field,
                      options: field.options?.map((current) =>
                        current.id === option.id
                          ? { ...current, label: event.target.value }
                          : current
                      ),
                    })
                  }
                  type="text"
                  value={option.label}
                />
                <button
                  aria-label={`Remove choice ${index + 1}`}
                  className="btn btn-ghost btn-sm text-error"
                  disabled={(field.options?.length ?? 0) <= 1}
                  onClick={() =>
                    onChange({
                      ...field,
                      options: field.options?.filter(
                        (current) => current.id !== option.id
                      ),
                    })
                  }
                  type="button"
                >
                  Remove
                </button>
              </div>
            </div>
          ))}
          <button
            className="btn btn-outline btn-sm"
            disabled={(field.options?.length ?? 0) >= 100}
            onClick={() =>
              onChange({
                ...field,
                options: [
                  ...(field.options ?? []),
                  {
                    id: `option_${crypto.randomUUID()}`,
                    label: `Option ${(field.options?.length ?? 0) + 1}`,
                  },
                ],
              })
            }
            type="button"
          >
            Add choice
          </button>
        </fieldset>
      )}
    </fieldset>
  );
}
