import { useForm } from '@tanstack/react-form';
import { useEffect, useId, useState } from 'react';
import type { FormDefinition } from './models/FormDefinition.ts';
import type { FormField } from './models/FormField.ts';

function validateAnswer(field: FormField, answer: string | string[]) {
  if (field.type === 'checkboxes') {
    return field.validation?.required &&
      (!Array.isArray(answer) || !answer.length)
      ? 'Select at least one option.'
      : undefined;
  }
  const text = typeof answer === 'string' ? answer : '';
  if (field.validation?.required && !text.trim()) {
    return 'This field is required.';
  }
  if (
    text.length &&
    field.validation?.minLength !== undefined &&
    text.length < field.validation.minLength
  ) {
    return `Enter at least ${field.validation.minLength} characters.`;
  }
  if (
    field.validation?.maxLength !== undefined &&
    text.length > field.validation.maxLength
  ) {
    return `Use ${field.validation.maxLength} characters or fewer.`;
  }
  return undefined;
}

export function FormPreview({ definition }: { definition: FormDefinition }) {
  const prefix = useId();
  const [validatedAnswers, setValidatedAnswers] = useState<
    (string | string[])[] | null
  >(null);
  const form = useForm({
    defaultValues: {
      answers: definition.fields.map((field): string | string[] =>
        field.type === 'checkboxes' ? [] : ''
      ),
    },
    onSubmit: ({ value }) => setValidatedAnswers(value.answers),
  });

  useEffect(() => {
    form.reset({
      answers: definition.fields.map((field) =>
        field.type === 'checkboxes' ? [] : ''
      ),
    });
  }, [definition, form]);

  return (
    <section
      aria-label="Form preview"
      className="rounded-xl border border-base-300 bg-base-100 p-5 sm:p-6"
    >
      <h2 className="text-xl font-semibold text-primary">Preview</h2>
      <p className="mt-1 text-sm text-base-content/65">
        Try the form as an end user. Preview answers are not saved.
      </p>
      <form
        className="mt-6 space-y-6"
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          event.stopPropagation();
          setValidatedAnswers(null);
          void form.validateAllFields('submit').then(() => form.handleSubmit());
        }}
      >
        {!definition.fields.length && (
          <p className="text-base-content/65">
            Add fields to preview your form.
          </p>
        )}
        {definition.fields.map((definitionField, index) => {
          const inputId = `${prefix}-${definitionField.id}`;
          const helpId = `${inputId}-help`;
          const errorId = `${inputId}-error`;
          if (definitionField.type === 'text') {
            return (
              <div className="space-y-2 break-words" key={definitionField.id}>
                <p className="whitespace-pre-wrap">{definitionField.label}</p>
                {definitionField.helpText && (
                  <p className="whitespace-pre-wrap text-sm text-base-content/65">
                    {definitionField.helpText}
                  </p>
                )}
              </div>
            );
          }
          return (
            <form.Field
              key={definitionField.id}
              name={`answers[${index}]`}
              validators={{
                onChange: ({ value }) => validateAnswer(definitionField, value),
                onSubmit: ({ value }) => validateAnswer(definitionField, value),
              }}
            >
              {(field) => {
                const hasError =
                  field.state.meta.isTouched && !field.state.meta.isValid;
                const isGroup =
                  definitionField.type === 'checkboxes' ||
                  definitionField.type === 'radio';
                const describedBy =
                  [
                    definitionField.helpText ? helpId : '',
                    hasError ? errorId : '',
                  ]
                    .filter(Boolean)
                    .join(' ') || undefined;
                const answer =
                  typeof field.state.value === 'string'
                    ? field.state.value
                    : '';
                const selections = Array.isArray(field.state.value)
                  ? field.state.value
                  : [];
                const change = (value: string | string[]) => {
                  setValidatedAnswers(null);
                  field.handleChange(value);
                };
                const caption = (
                  <>
                    {definitionField.label}
                    {definitionField.validation?.required && (
                      <span className="ml-1 text-sm font-normal">
                        (required)
                      </span>
                    )}
                  </>
                );
                return (
                  <div>
                    {isGroup ? (
                      <fieldset
                        aria-describedby={describedBy}
                        aria-invalid={hasError || undefined}
                      >
                        <legend className="mb-3 whitespace-pre-wrap break-words font-semibold">
                          {caption}
                        </legend>
                        <div className="space-y-3">
                          {(definitionField.options ?? []).map((option) => (
                            <label
                              className="flex items-start gap-3"
                              key={option.id}
                            >
                              <input
                                checked={
                                  definitionField.type === 'checkboxes'
                                    ? selections.includes(option.id)
                                    : answer === option.id
                                }
                                className={
                                  definitionField.type === 'checkboxes'
                                    ? 'checkbox checkbox-primary'
                                    : 'radio radio-primary'
                                }
                                name={field.name}
                                onBlur={field.handleBlur}
                                onChange={(event) =>
                                  change(
                                    definitionField.type === 'checkboxes'
                                      ? event.target.checked
                                        ? [...selections, option.id]
                                        : selections.filter(
                                            (id) => id !== option.id
                                          )
                                      : option.id
                                  )
                                }
                                type={
                                  definitionField.type === 'checkboxes'
                                    ? 'checkbox'
                                    : 'radio'
                                }
                                value={option.id}
                              />
                              <span className="min-w-0 break-words">
                                {option.label}
                              </span>
                            </label>
                          ))}
                        </div>
                        {definitionField.type === 'radio' &&
                          !definitionField.validation?.required &&
                          answer && (
                            <button
                              className="btn btn-ghost btn-xs mt-2"
                              onClick={() => change('')}
                              type="button"
                            >
                              Clear selection
                            </button>
                          )}
                      </fieldset>
                    ) : (
                      <>
                        <label
                          className="mb-2 block whitespace-pre-wrap break-words font-semibold"
                          htmlFor={inputId}
                        >
                          {caption}
                        </label>
                        {definitionField.type === 'dropdown' ? (
                          <select
                            aria-describedby={describedBy}
                            aria-invalid={hasError || undefined}
                            className={`select select-bordered w-full ${hasError ? 'select-error' : ''}`}
                            id={inputId}
                            name={field.name}
                            onBlur={field.handleBlur}
                            onChange={(event) => change(event.target.value)}
                            required={definitionField.validation?.required}
                            value={answer}
                          >
                            <option value="">Select an option</option>
                            {(definitionField.options ?? []).map((option) => (
                              <option key={option.id} value={option.id}>
                                {option.label}
                              </option>
                            ))}
                          </select>
                        ) : definitionField.type === 'textarea' ? (
                          <textarea
                            aria-describedby={describedBy}
                            aria-invalid={hasError || undefined}
                            className={`textarea textarea-bordered w-full ${hasError ? 'textarea-error' : ''}`}
                            id={inputId}
                            maxLength={definitionField.validation?.maxLength}
                            minLength={definitionField.validation?.minLength}
                            name={field.name}
                            onBlur={field.handleBlur}
                            onChange={(event) => change(event.target.value)}
                            required={definitionField.validation?.required}
                            rows={4}
                            value={answer}
                          />
                        ) : (
                          <input
                            aria-describedby={describedBy}
                            aria-invalid={hasError || undefined}
                            className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                            id={inputId}
                            maxLength={definitionField.validation?.maxLength}
                            minLength={definitionField.validation?.minLength}
                            name={field.name}
                            onBlur={field.handleBlur}
                            onChange={(event) => change(event.target.value)}
                            required={definitionField.validation?.required}
                            type="text"
                            value={answer}
                          />
                        )}
                      </>
                    )}
                    {definitionField.helpText && (
                      <p
                        className="mt-2 whitespace-pre-wrap break-words text-sm text-base-content/65"
                        id={helpId}
                      >
                        {definitionField.helpText}
                      </p>
                    )}
                    {hasError && (
                      <p
                        className="mt-2 text-sm text-error"
                        id={errorId}
                        role="alert"
                      >
                        {[...new Set(field.state.meta.errors)].join(' ')}
                      </p>
                    )}
                  </div>
                );
              }}
            </form.Field>
          );
        })}
        {definition.fields.some((field) => field.type !== 'text') && (
          <button className="btn btn-outline btn-primary" type="submit">
            Try validation
          </button>
        )}
        <form.Subscribe selector={(state) => state.values.answers}>
          {(answers) =>
            validatedAnswers === answers && (
              <p className="alert alert-success" role="status">
                Preview passes validation. No responses have been saved.
              </p>
            )
          }
        </form.Subscribe>
      </form>
    </section>
  );
}
