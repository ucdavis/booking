import { useId } from 'react';
import { useStore } from '@tanstack/react-form';
import type { BillingRate } from './models/BillingRate.ts';
import { createBillingRate } from './resourceDefaults.ts';
import { withFieldGroup } from '@/shared/forms/formContext.tsx';

export const BillingRatesEditor = withFieldGroup({
  defaultValues: {
    billingRates: null as
      | Pick<BillingRate, 'amount' | 'basis' | 'id' | 'name'>[]
      | null,
  },
  props: { disabled: false, readOnly: false },
  render: function BillingRatesEditor({ disabled, group, readOnly }) {
    const prefix = useId();
    const hasSubmitted = useStore(
      group.form.store,
      (state) => state.submissionAttempts > 0
    );

    return (
      <group.Field mode="array" name="billingRates">
        {(ratesField) => {
          const billingRates = ratesField.state.value;
          const hasError =
            (ratesField.state.meta.isTouched || hasSubmitted) &&
            !ratesField.state.meta.isValid;
          return (
            <fieldset
              className="min-w-0 space-y-4"
              disabled={disabled || readOnly}
            >
              <legend className="mb-3 text-lg font-semibold text-primary">
                Billing rates
              </legend>
              <label className="flex items-center gap-3 text-sm font-semibold">
                <input
                  checked={billingRates !== null}
                  className="checkbox checkbox-primary"
                  name={ratesField.name}
                  onBlur={ratesField.handleBlur}
                  onChange={(event) =>
                    ratesField.handleChange(
                      event.target.checked ? [createBillingRate()] : null
                    )
                  }
                  type="checkbox"
                />
                Include default billing rates
              </label>
              {billingRates !== null && (
                <>
                  <p
                    className="text-sm text-base-content/65"
                    id={`${prefix}-help`}
                  >
                    Enter a rate name and a nonnegative amount in US dollars.
                    Rates can apply per hour, per day, or per booking.
                  </p>
                  {billingRates.length === 0 && (
                    <p className="rounded-lg border border-dashed border-base-300 p-4 text-sm text-base-content/65">
                      No billing rates are configured.
                    </p>
                  )}
                  <div className="space-y-3">
                    {billingRates.map((rate, index) => (
                      <div
                        className="rounded-lg border border-base-300 p-4"
                        key={rate.id}
                      >
                        <div className="grid items-end gap-3 sm:grid-cols-2 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)_auto]">
                          <group.Field name={`billingRates[${index}].name`}>
                            {(field) => {
                              const hasError =
                                (field.state.meta.isTouched || hasSubmitted) &&
                                !field.state.meta.isValid;
                              const errorId = `${prefix}-${rate.id}-name-error`;
                              return (
                                <div className="min-w-0">
                                  <label
                                    className="mb-1 block text-sm font-medium"
                                    htmlFor={`${prefix}-${rate.id}-name`}
                                  >
                                    Rate name {index + 1}
                                  </label>
                                  <input
                                    aria-describedby={
                                      hasError ? errorId : undefined
                                    }
                                    aria-invalid={hasError || undefined}
                                    className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                                    id={`${prefix}-${rate.id}-name`}
                                    name={field.name}
                                    onBlur={field.handleBlur}
                                    onChange={(event) =>
                                      field.handleChange(event.target.value)
                                    }
                                    value={field.state.value ?? ''}
                                  />
                                  {hasError && (
                                    <p
                                      className="mt-2 text-sm text-error"
                                      id={errorId}
                                      role="alert"
                                    >
                                      {field.state.meta.errors.join(' ')}
                                    </p>
                                  )}
                                </div>
                              );
                            }}
                          </group.Field>
                          <group.Field name={`billingRates[${index}].amount`}>
                            {(field) => {
                              const hasError =
                                (field.state.meta.isTouched || hasSubmitted) &&
                                !field.state.meta.isValid;
                              const errorId = `${prefix}-${rate.id}-amount-error`;
                              return (
                                <div className="min-w-0">
                                  <label
                                    className="mb-1 block text-sm font-medium"
                                    htmlFor={`${prefix}-${rate.id}-amount`}
                                  >
                                    Amount (USD) {index + 1}
                                  </label>
                                  <input
                                    aria-describedby={`${prefix}-help${hasError ? ` ${errorId}` : ''}`}
                                    aria-invalid={hasError || undefined}
                                    className={`input input-bordered w-full ${hasError ? 'input-error' : ''}`}
                                    id={`${prefix}-${rate.id}-amount`}
                                    inputMode="decimal"
                                    name={field.name}
                                    onBlur={field.handleBlur}
                                    onChange={(event) =>
                                      field.handleChange(event.target.value)
                                    }
                                    type="text"
                                    value={field.state.value ?? ''}
                                  />
                                  {hasError && (
                                    <p
                                      className="mt-2 text-sm text-error"
                                      id={errorId}
                                      role="alert"
                                    >
                                      {field.state.meta.errors.join(' ')}
                                    </p>
                                  )}
                                </div>
                              );
                            }}
                          </group.Field>
                          <group.Field name={`billingRates[${index}].basis`}>
                            {(field) => {
                              const hasError =
                                (field.state.meta.isTouched || hasSubmitted) &&
                                !field.state.meta.isValid;
                              const errorId = `${prefix}-${rate.id}-basis-error`;
                              return (
                                <div className="min-w-0">
                                  <label
                                    className="mb-1 block text-sm font-medium"
                                    htmlFor={`${prefix}-${rate.id}-basis`}
                                  >
                                    Rate basis {index + 1}
                                  </label>
                                  <select
                                    aria-describedby={
                                      hasError ? errorId : undefined
                                    }
                                    aria-invalid={hasError || undefined}
                                    className={`select select-bordered w-full ${hasError ? 'select-error' : ''}`}
                                    id={`${prefix}-${rate.id}-basis`}
                                    name={field.name}
                                    onBlur={field.handleBlur}
                                    onChange={(event) =>
                                      field.handleChange(
                                        event.target
                                          .value as BillingRate['basis']
                                      )
                                    }
                                    value={field.state.value ?? ''}
                                  >
                                    <option value="hour">Per hour</option>
                                    <option value="day">Per day</option>
                                    <option value="booking">Per booking</option>
                                  </select>
                                  {hasError && (
                                    <p
                                      className="mt-2 text-sm text-error"
                                      id={errorId}
                                      role="alert"
                                    >
                                      {field.state.meta.errors.join(' ')}
                                    </p>
                                  )}
                                </div>
                              );
                            }}
                          </group.Field>
                          {!readOnly && (
                            <button
                              aria-label={`Remove billing rate ${index + 1}`}
                              className="btn btn-ghost btn-sm text-error"
                              onClick={() => ratesField.removeValue(index)}
                              type="button"
                            >
                              Remove
                            </button>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                  {!readOnly && (
                    <button
                      className="btn btn-outline btn-sm"
                      onClick={() => ratesField.pushValue(createBillingRate())}
                      type="button"
                    >
                      Add billing rate
                    </button>
                  )}
                </>
              )}
              {hasError && (
                <p className="text-sm text-error" role="alert">
                  {ratesField.state.meta.errors.join(' ')}
                </p>
              )}
            </fieldset>
          );
        }}
      </group.Field>
    );
  },
});
