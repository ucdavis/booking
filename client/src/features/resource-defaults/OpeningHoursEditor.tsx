import { useId } from 'react';
import { useStore } from '@tanstack/react-form';
import type { OpeningHoursInterval } from './models/OpeningHoursInterval.ts';
import { withFieldGroup } from '@/shared/forms/formContext.tsx';
import {
  createOpeningHoursInterval,
  resourceDefaultsDays,
  resourceDefaultsFormValues,
} from './resourceDefaults.ts';

export const OpeningHoursEditor = withFieldGroup({
  defaultValues: {
    openingHours: null as Partial<
      Record<
        (typeof resourceDefaultsDays)[number]['key'],
        Pick<OpeningHoursInterval, 'start' | 'end'>[] | null
      >
    > | null,
  },
  props: { disabled: false, readOnly: false },
  render: function OpeningHoursEditor({ disabled, group, readOnly }) {
    const prefix = useId();
    const hasSubmitted = useStore(
      group.form.store,
      (state) => state.submissionAttempts > 0
    );

    return (
      <group.Field name="openingHours">
        {(hoursField) => {
          const openingHours = hoursField.state.value;
          return (
            <fieldset
              className="min-w-0 space-y-4"
              disabled={disabled || readOnly}
            >
              <legend className="mb-3 text-lg font-semibold text-primary">
                Opening hours
              </legend>
              <label className="flex items-center gap-3 text-sm font-semibold">
                <input
                  checked={openingHours !== null}
                  className="checkbox checkbox-primary"
                  onBlur={hoursField.handleBlur}
                  onChange={(event) =>
                    hoursField.handleChange(
                      event.target.checked
                        ? resourceDefaultsFormValues({ openingHours: {} })
                            .openingHours
                        : null
                    )
                  }
                  type="checkbox"
                />
                Include default opening hours
              </label>
              {openingHours !== null && (
                <>
                  <p
                    className="text-sm text-base-content/65"
                    id={`${prefix}-help`}
                  >
                    Use local times in the resource space&apos;s time zone. No
                    default leaves a day unspecified; Closed sets it as closed.
                    Each period stays within its selected day, with closing time
                    after opening time.
                  </p>
                  <div className="divide-y divide-base-300 rounded-lg border border-base-300">
                    {resourceDefaultsDays.map((day) => (
                      <group.Field
                        key={day.key}
                        mode="array"
                        name={`openingHours.${day.key}`}
                      >
                        {(dayField) => {
                          const intervals = dayField.state.value;
                          return (
                            <div className="space-y-3 p-4">
                              <div className="flex flex-wrap items-center gap-3">
                                <label
                                  className="w-24 text-sm font-semibold"
                                  htmlFor={`${prefix}-${day.key}`}
                                >
                                  {day.label}
                                </label>
                                <select
                                  aria-describedby={`${prefix}-help`}
                                  aria-label={`${day.label} hours`}
                                  className="select select-bordered w-full sm:w-44"
                                  id={`${prefix}-${day.key}`}
                                  onBlur={dayField.handleBlur}
                                  onChange={(event) =>
                                    dayField.handleChange(
                                      event.target.value === 'unspecified'
                                        ? null
                                        : event.target.value === 'closed'
                                          ? []
                                          : [createOpeningHoursInterval()]
                                    )
                                  }
                                  value={
                                    intervals == null
                                      ? 'unspecified'
                                      : intervals.length
                                        ? 'open'
                                        : 'closed'
                                  }
                                >
                                  <option value="unspecified">
                                    No default
                                  </option>
                                  <option value="closed">Closed</option>
                                  <option value="open">Set hours</option>
                                </select>
                              </div>
                              {intervals?.map((_, index) => (
                                <div
                                  className="flex flex-wrap items-end gap-3"
                                  key={`${day.key}-${index}`}
                                >
                                  <group.Field
                                    name={`openingHours.${day.key}[${index}].start`}
                                  >
                                    {(field) => {
                                      const hasError =
                                        (field.state.meta.isTouched ||
                                          hasSubmitted) &&
                                        !field.state.meta.isValid;
                                      return (
                                        <div className="min-w-0 flex-1 sm:flex-none">
                                          <label
                                            className="mb-1 block text-xs font-medium"
                                            htmlFor={`${prefix}-${day.key}-start-${index}`}
                                          >
                                            Opening time
                                          </label>
                                          <input
                                            aria-describedby={
                                              hasError
                                                ? `${prefix}-${day.key}-start-${index}-error`
                                                : undefined
                                            }
                                            aria-invalid={hasError || undefined}
                                            aria-label={`${day.label} opening time ${index + 1}`}
                                            className="input input-bordered w-full sm:w-36"
                                            id={`${prefix}-${day.key}-start-${index}`}
                                            onBlur={field.handleBlur}
                                            onChange={(event) =>
                                              field.handleChange(
                                                event.target.value
                                              )
                                            }
                                            type="time"
                                            value={field.state.value ?? ''}
                                          />
                                          {hasError && (
                                            <p
                                              className="mt-2 text-sm text-error"
                                              id={`${prefix}-${day.key}-start-${index}-error`}
                                              role="alert"
                                            >
                                              {field.state.meta.errors.join(
                                                ' '
                                              )}
                                            </p>
                                          )}
                                        </div>
                                      );
                                    }}
                                  </group.Field>
                                  <group.Field
                                    name={`openingHours.${day.key}[${index}].end`}
                                  >
                                    {(field) => {
                                      const hasError =
                                        (field.state.meta.isTouched ||
                                          hasSubmitted) &&
                                        !field.state.meta.isValid;
                                      return (
                                        <div className="min-w-0 flex-1 sm:flex-none">
                                          <label
                                            className="mb-1 block text-xs font-medium"
                                            htmlFor={`${prefix}-${day.key}-end-${index}`}
                                          >
                                            Closing time
                                          </label>
                                          <input
                                            aria-describedby={
                                              hasError
                                                ? `${prefix}-${day.key}-end-${index}-error`
                                                : undefined
                                            }
                                            aria-invalid={hasError || undefined}
                                            aria-label={`${day.label} closing time ${index + 1}`}
                                            className="input input-bordered w-full sm:w-36"
                                            id={`${prefix}-${day.key}-end-${index}`}
                                            onBlur={field.handleBlur}
                                            onChange={(event) =>
                                              field.handleChange(
                                                event.target.value
                                              )
                                            }
                                            type="time"
                                            value={field.state.value ?? ''}
                                          />
                                          {hasError && (
                                            <p
                                              className="mt-2 text-sm text-error"
                                              id={`${prefix}-${day.key}-end-${index}-error`}
                                              role="alert"
                                            >
                                              {field.state.meta.errors.join(
                                                ' '
                                              )}
                                            </p>
                                          )}
                                        </div>
                                      );
                                    }}
                                  </group.Field>
                                  {!readOnly && (
                                    <button
                                      aria-label={`Remove ${day.label} hours ${index + 1}`}
                                      className="btn btn-ghost btn-sm text-error"
                                      onClick={() =>
                                        void dayField.removeValue(index)
                                      }
                                      type="button"
                                    >
                                      Remove
                                    </button>
                                  )}
                                </div>
                              ))}
                              {!readOnly &&
                                intervals != null &&
                                intervals.length > 0 && (
                                  <button
                                    className="btn btn-outline btn-sm"
                                    onClick={() =>
                                      dayField.pushValue(
                                        createOpeningHoursInterval()
                                      )
                                    }
                                    type="button"
                                  >
                                    Add {day.label} hours
                                  </button>
                                )}
                            </div>
                          );
                        }}
                      </group.Field>
                    ))}
                  </div>
                </>
              )}
            </fieldset>
          );
        }}
      </group.Field>
    );
  },
});
