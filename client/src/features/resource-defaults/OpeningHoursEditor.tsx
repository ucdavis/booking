import { useId } from 'react';
import type { OpeningHoursInterval } from './models/OpeningHoursInterval.ts';
import type { ResourceDefaults } from './models/ResourceDefaults.ts';
import {
  createOpeningHoursInterval,
  resourceDefaultsDays,
} from './resourceDefaults.ts';

export function OpeningHoursEditor({
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
  const { openingHours, ...defaultsWithoutHours } = value;
  const updateDay = (
    day: (typeof resourceDefaultsDays)[number]['key'],
    intervals: OpeningHoursInterval[] | undefined
  ) => {
    const nextHours = { ...openingHours };
    if (intervals === undefined) {
      delete nextHours[day];
    } else {
      nextHours[day] = intervals;
    }
    onChange({ ...value, openingHours: nextHours });
  };

  return (
    <fieldset className="min-w-0 space-y-4" disabled={disabled || readOnly}>
      <legend className="mb-3 text-lg font-semibold text-primary">
        Opening hours
      </legend>
      <label className="flex items-center gap-3 text-sm font-semibold">
        <input
          checked={openingHours !== undefined}
          className="checkbox checkbox-primary"
          onChange={(event) =>
            onChange(
              event.target.checked
                ? { ...value, openingHours: {} }
                : defaultsWithoutHours
            )
          }
          type="checkbox"
        />
        Include default opening hours
      </label>
      {openingHours !== undefined && (
        <>
          <p className="text-sm text-base-content/65" id={`${prefix}-help`}>
            Use local times in the resource space&apos;s time zone. No default
            leaves a day unspecified; Closed sets it as closed. Each period
            stays within its selected day, with closing time after opening time.
          </p>
          <div className="divide-y divide-base-300 rounded-lg border border-base-300">
            {resourceDefaultsDays.map((day) => {
              const intervals = openingHours[day.key];
              return (
                <div className="space-y-3 p-4" key={day.key}>
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
                      onChange={(event) =>
                        updateDay(
                          day.key,
                          event.target.value === 'unspecified'
                            ? undefined
                            : event.target.value === 'closed'
                              ? []
                              : [createOpeningHoursInterval()]
                        )
                      }
                      value={
                        intervals === undefined
                          ? 'unspecified'
                          : intervals.length
                            ? 'open'
                            : 'closed'
                      }
                    >
                      <option value="unspecified">No default</option>
                      <option value="closed">Closed</option>
                      <option value="open">Set hours</option>
                    </select>
                  </div>
                  {intervals?.map((interval, index) => (
                    <div
                      className="flex flex-wrap items-end gap-3"
                      key={`${day.key}-${index}`}
                    >
                      <div className="min-w-0 flex-1 sm:flex-none">
                        <label
                          className="mb-1 block text-xs font-medium"
                          htmlFor={`${prefix}-${day.key}-start-${index}`}
                        >
                          Opening time
                        </label>
                        <input
                          aria-label={`${day.label} opening time ${index + 1}`}
                          className="input input-bordered w-full sm:w-36"
                          id={`${prefix}-${day.key}-start-${index}`}
                          onChange={(event) =>
                            updateDay(
                              day.key,
                              intervals.map((current, currentIndex) =>
                                currentIndex === index
                                  ? { ...current, start: event.target.value }
                                  : current
                              )
                            )
                          }
                          type="time"
                          value={interval.start}
                        />
                      </div>
                      <div className="min-w-0 flex-1 sm:flex-none">
                        <label
                          className="mb-1 block text-xs font-medium"
                          htmlFor={`${prefix}-${day.key}-end-${index}`}
                        >
                          Closing time
                        </label>
                        <input
                          aria-label={`${day.label} closing time ${index + 1}`}
                          className="input input-bordered w-full sm:w-36"
                          id={`${prefix}-${day.key}-end-${index}`}
                          onChange={(event) =>
                            updateDay(
                              day.key,
                              intervals.map((current, currentIndex) =>
                                currentIndex === index
                                  ? { ...current, end: event.target.value }
                                  : current
                              )
                            )
                          }
                          type="time"
                          value={interval.end}
                        />
                      </div>
                      {!readOnly && (
                        <button
                          aria-label={`Remove ${day.label} hours ${index + 1}`}
                          className="btn btn-ghost btn-sm text-error"
                          onClick={() =>
                            updateDay(
                              day.key,
                              intervals.filter(
                                (_, currentIndex) => currentIndex !== index
                              )
                            )
                          }
                          type="button"
                        >
                          Remove
                        </button>
                      )}
                    </div>
                  ))}
                  {!readOnly &&
                    intervals !== undefined &&
                    intervals.length > 0 && (
                      <button
                        className="btn btn-outline btn-sm"
                        onClick={() =>
                          updateDay(day.key, [
                            ...intervals,
                            createOpeningHoursInterval(),
                          ])
                        }
                        type="button"
                      >
                        Add {day.label} hours
                      </button>
                    )}
                </div>
              );
            })}
          </div>
        </>
      )}
    </fieldset>
  );
}
