import { useId } from 'react';
import type { BillingRate } from './models/BillingRate.ts';
import type { ResourceDefaults } from './models/ResourceDefaults.ts';
import { createBillingRate } from './resourceDefaults.ts';

export function BillingRatesEditor({
  disabled = false,
  onChange,
  readOnly = false,
  value,
}: {
  disabled?: boolean;
  onChange: (value: ResourceDefaults) => void;
  readOnly?: boolean;
  value: ResourceDefaults;
}) {
  const prefix = useId();
  const { billingRates, ...defaultsWithoutRates } = value;
  const updateRate = (id: string, changes: Partial<BillingRate>) =>
    onChange({
      ...value,
      billingRates: billingRates?.map((rate) =>
        rate.id === id ? { ...rate, ...changes } : rate
      ),
    });

  return (
    <fieldset className="min-w-0 space-y-4" disabled={disabled || readOnly}>
      <legend className="mb-3 text-lg font-semibold text-primary">
        Billing rates
      </legend>
      <label className="flex items-center gap-3 text-sm font-semibold">
        <input
          checked={billingRates !== undefined}
          className="checkbox checkbox-primary"
          onChange={(event) =>
            onChange(
              event.target.checked
                ? {
                    ...value,
                    billingRates: [createBillingRate()],
                  }
                : defaultsWithoutRates
            )
          }
          type="checkbox"
        />
        Include default billing rates
      </label>
      {billingRates !== undefined && (
        <>
          <p className="text-sm text-base-content/65" id={`${prefix}-help`}>
            Enter a rate name and a nonnegative amount in US dollars. Rates can
            apply per hour, per day, or per booking.
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
                  <div className="min-w-0">
                    <label
                      className="mb-1 block text-sm font-medium"
                      htmlFor={`${prefix}-${rate.id}-name`}
                    >
                      Rate name {index + 1}
                    </label>
                    <input
                      className="input input-bordered w-full"
                      id={`${prefix}-${rate.id}-name`}
                      onChange={(event) =>
                        updateRate(rate.id, { name: event.target.value })
                      }
                      value={rate.name}
                    />
                  </div>
                  <div className="min-w-0">
                    <label
                      className="mb-1 block text-sm font-medium"
                      htmlFor={`${prefix}-${rate.id}-amount`}
                    >
                      Amount (USD) {index + 1}
                    </label>
                    <input
                      aria-describedby={`${prefix}-help`}
                      className="input input-bordered w-full"
                      id={`${prefix}-${rate.id}-amount`}
                      inputMode="decimal"
                      onChange={(event) =>
                        updateRate(rate.id, { amount: event.target.value })
                      }
                      type="text"
                      value={rate.amount}
                    />
                  </div>
                  <div className="min-w-0">
                    <label
                      className="mb-1 block text-sm font-medium"
                      htmlFor={`${prefix}-${rate.id}-basis`}
                    >
                      Rate basis {index + 1}
                    </label>
                    <select
                      className="select select-bordered w-full"
                      id={`${prefix}-${rate.id}-basis`}
                      onChange={(event) =>
                        updateRate(rate.id, {
                          basis: event.target.value as BillingRate['basis'],
                        })
                      }
                      value={rate.basis}
                    >
                      <option value="hour">Per hour</option>
                      <option value="day">Per day</option>
                      <option value="booking">Per booking</option>
                    </select>
                  </div>
                  {!readOnly && (
                    <button
                      aria-label={`Remove billing rate ${index + 1}`}
                      className="btn btn-ghost btn-sm text-error"
                      onClick={() =>
                        onChange({
                          ...value,
                          billingRates: billingRates.filter(
                            (current) => current.id !== rate.id
                          ),
                        })
                      }
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
              onClick={() =>
                onChange({
                  ...value,
                  billingRates: [...billingRates, createBillingRate()],
                })
              }
              type="button"
            >
              Add billing rate
            </button>
          )}
        </>
      )}
    </fieldset>
  );
}
