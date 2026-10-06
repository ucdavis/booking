import type { BillingRate } from './models/BillingRate.ts';
import type { OpeningHoursInterval } from './models/OpeningHoursInterval.ts';
import type { ResourceDefaults } from './models/ResourceDefaults.ts';

const maxJsonLength = 1024 * 1024;

export const resourceDefaultsDays = [
  { key: 'monday', label: 'Monday' },
  { key: 'tuesday', label: 'Tuesday' },
  { key: 'wednesday', label: 'Wednesday' },
  { key: 'thursday', label: 'Thursday' },
  { key: 'friday', label: 'Friday' },
  { key: 'saturday', label: 'Saturday' },
  { key: 'sunday', label: 'Sunday' },
] as const;

// A mounted TanStack field restores its initial value when set to undefined.
// Use null for explicitly cleared optional controls and omit it when saving.
export function resourceDefaultsFormValues(defaults: ResourceDefaults) {
  const days = Object.fromEntries(
    resourceDefaultsDays.map(({ key }) => [
      key,
      defaults.openingHours?.[key] ?? null,
    ])
  ) as Record<
    (typeof resourceDefaultsDays)[number]['key'],
    OpeningHoursInterval[] | null
  >;
  return {
    ...defaults,
    billingRates: defaults.billingRates ?? null,
    openingHours:
      defaults.openingHours === undefined
        ? null
        : { ...defaults.openingHours, ...days },
  };
}

export function resourceDefaultsFromForm(
  values: ReturnType<typeof resourceDefaultsFormValues>
): ResourceDefaults {
  const { billingRates, openingHours, ...defaults } = values;
  return {
    ...defaults,
    ...(billingRates === null ? {} : { billingRates }),
    ...(openingHours === null
      ? {}
      : {
          openingHours: Object.fromEntries(
            Object.entries(openingHours).filter(
              ([key, value]) =>
                !resourceDefaultsDays.some((day) => day.key === key) ||
                value !== null
            )
          ),
        }),
  };
}

function isObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

// Compare the exact decimal values of JSON number tokens without converting the
// source to a floating-point number. This also handles exponent notation.
function normalizeNumber(token: string): string {
  const match = /^(-?)(\d+)(?:\.(\d+))?(?:[Ee]([+-]?\d+))?$/.exec(token)!;
  const fraction = match[3] ?? '';
  const digits = (match[2] + fraction).replace(/^0+/, '');
  if (!digits) {
    return `${match[1]}0`;
  }
  const significant = digits.replace(/0+$/, '');
  const exponent =
    BigInt(match[4] ?? '0') -
    BigInt(fraction.length) +
    BigInt(digits.length - significant.length);
  return `${match[1]}${significant}e${exponent}`;
}

function canRoundTripJson(source: string): boolean {
  const properties: (Set<string> | null)[] = [];
  // JSON.parse has already checked syntax. Consume complete string tokens so
  // punctuation, numeric text, and escaped property names are handled correctly.
  const tokens =
    /("(?:\\.|[^"\\])*")(\s*:)?|(-?\d+(?:\.\d+)?(?:[Ee][+-]?\d+)?)|([[\]{}])/g;
  for (const token of source.matchAll(tokens)) {
    if (token[4] === '{') {
      properties.push(new Set());
      if (properties.length > 64) {
        return false;
      }
    } else if (token[4] === '[') {
      properties.push(null);
      if (properties.length > 64) {
        return false;
      }
    } else if (token[4] === '}' || token[4] === ']') {
      properties.pop();
    } else if (token[2]) {
      const names = properties.at(-1);
      const name: string = JSON.parse(token[1]);
      if (names?.has(name)) {
        return false;
      }
      names?.add(name);
    } else if (token[3]) {
      const number = Number(token[3]);
      if (!Number.isFinite(number)) {
        return false;
      }
      // Avoid constructing enormous exponents for underflowed numbers.
      if (number === 0 && /[1-9]/.test(token[3].split(/[Ee]/)[0])) {
        return false;
      }
      if (
        normalizeNumber(token[3]) !== normalizeNumber(JSON.stringify(number))
      ) {
        return false;
      }
    }
  }
  return true;
}

function hasSupportedShape(value: Record<string, unknown>): boolean {
  if ('schemaVersion' in value && value.schemaVersion !== 1) {
    return false;
  }
  if ('openingHours' in value) {
    if (!isObject(value.openingHours)) {
      return false;
    }
    for (const { key } of resourceDefaultsDays) {
      if (!(key in value.openingHours)) {
        continue;
      }
      const intervals = value.openingHours[key];
      if (!Array.isArray(intervals)) {
        return false;
      }
      if (
        intervals.some(
          (interval: unknown) =>
            !isObject(interval) ||
            typeof interval.start !== 'string' ||
            typeof interval.end !== 'string'
        )
      ) {
        return false;
      }
    }
  }
  if ('billingRates' in value) {
    if (!Array.isArray(value.billingRates)) {
      return false;
    }
    if (
      value.billingRates.some(
        (rate: unknown) =>
          !isObject(rate) ||
          typeof rate.id !== 'string' ||
          typeof rate.name !== 'string' ||
          typeof rate.amount !== 'string' ||
          ('currency' in rate && rate.currency !== 'USD') ||
          !['hour', 'day', 'booking'].includes(rate.basis as string)
      )
    ) {
      return false;
    }
    const ids = value.billingRates.map((rate) => (rate as BillingRate).id);
    if (ids.some((id) => !id.trim()) || new Set(ids).size !== ids.length) {
      return false;
    }
  }
  return true;
}

export function parseResourceDefaultsJson(source: string | null | undefined): {
  defaults: ResourceDefaults;
  unavailableReason?: string;
} {
  if (!source?.trim()) {
    return { defaults: {} };
  }
  const unavailable = (reason: string) => ({
    defaults: {} as ResourceDefaults,
    unavailableReason: `${reason} The saved defaults will be preserved when you save the template.`,
  });
  if (source.length > maxJsonLength) {
    return unavailable(
      'These defaults exceed the size supported by this editor.'
    );
  }
  let value: unknown;
  try {
    value = JSON.parse(source);
  } catch {
    return unavailable('These saved defaults are not valid JSON.');
  }
  if (!isObject(value) || !hasSupportedShape(value)) {
    return unavailable(
      'These saved defaults use a format this editor does not support.'
    );
  }
  if (!canRoundTripJson(source)) {
    return unavailable(
      'These saved defaults contain values that cannot be edited without losing data.'
    );
  }
  return { defaults: value as ResourceDefaults };
}

function equivalentJson(left: unknown, right: unknown): boolean {
  if (Object.is(left, right)) {
    return true;
  }
  if (Array.isArray(left) && Array.isArray(right)) {
    return (
      left.length === right.length &&
      left.every((value, index) => equivalentJson(value, right[index]))
    );
  }
  if (!isObject(left) || !isObject(right)) {
    return false;
  }
  const leftKeys = Object.keys(left).filter((key) => left[key] !== undefined);
  const rightKeys = Object.keys(right).filter(
    (key) => right[key] !== undefined
  );
  return (
    leftKeys.length === rightKeys.length &&
    leftKeys.every(
      (key) =>
        Object.hasOwn(right, key) && equivalentJson(left[key], right[key])
    )
  );
}

export function serializeResourceDefaults(
  defaults: ResourceDefaults,
  originalJson: string | null | undefined
): string | null {
  const original = parseResourceDefaultsJson(originalJson);
  if (original.unavailableReason) {
    return originalJson ?? null;
  }
  if (equivalentJson(defaults, original.defaults)) {
    return originalJson?.trim() ? originalJson : null;
  }
  if (
    Object.keys(defaults).every(
      (key) => key === 'schemaVersion' || defaults[key] === undefined
    )
  ) {
    return null;
  }
  return JSON.stringify({ ...defaults, schemaVersion: 1 });
}

function timeInMinutes(value: string): number | undefined {
  if (!/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(value)) {
    return undefined;
  }
  const [hour, minute] = value.split(':').map(Number);
  return hour * 60 + minute;
}

export function validateResourceDefaults(defaults: ResourceDefaults): string[] {
  return [...new Set(Object.values(validateResourceDefaultsFields(defaults)))];
}

// Paths are relative to the defaults object so any owning form can bind them.
export function validateResourceDefaultsFields(
  defaults: ResourceDefaults
): Record<string, string> {
  const errors: Record<string, string> = {};
  resourceDefaultsDays.forEach(({ key, label }) => {
    const intervals: { end: number; index: number; start: number }[] = [];
    (defaults.openingHours?.[key] ?? []).forEach((interval, index) => {
      const start = timeInMinutes(interval.start);
      const end = timeInMinutes(interval.end);
      const prefix = `${label}, period ${index + 1}:`;
      const path = `openingHours.${key}[${index}]`;
      if (start === undefined || end === undefined) {
        const message = `${prefix} enter both an opening time and a closing time.`;
        if (start === undefined) {
          errors[`${path}.start`] = message;
        }
        if (end === undefined) {
          errors[`${path}.end`] = message;
        }
      } else if (end <= start) {
        errors[`${path}.end`] =
          `${prefix} closing time must be after opening time on the same day.`;
      } else {
        intervals.push({ end, index, start });
      }
    });
    intervals.sort((left, right) => left.start - right.start);
    let previous: (typeof intervals)[number] | undefined;
    intervals.forEach((interval) => {
      if (previous && interval.start < previous.end) {
        const message = `${label}: opening hours must not overlap.`;
        errors[`openingHours.${key}[${previous.index}].end`] = message;
        errors[`openingHours.${key}[${interval.index}].start`] = message;
      }
      if (!previous || interval.end > previous.end) {
        previous = interval;
      }
    });
  });

  const ids = new Set<string>();
  (defaults.billingRates ?? []).forEach((rate, index) => {
    const prefix = `Billing rate ${index + 1}:`;
    const path = `billingRates[${index}]`;
    if (!rate.id.trim() || ids.has(rate.id)) {
      errors[`${path}.id`] = `${prefix} each rate must have a unique ID.`;
    }
    ids.add(rate.id);
    if (!rate.name.trim() || rate.name.trim().length > 100) {
      errors[`${path}.name`] =
        `${prefix} enter a name of 100 characters or fewer.`;
    }
    if (!/^\d+(?:\.\d+)?$/.test(rate.amount)) {
      errors[`${path}.amount`] =
        `${prefix} enter a nonnegative amount, such as 25 or 25.50.`;
    }
    if ('currency' in rate && rate.currency !== 'USD') {
      errors[`${path}.currency`] =
        `${prefix} only US dollar rates are supported.`;
    }
    if (!['hour', 'day', 'booking'].includes(rate.basis)) {
      errors[`${path}.basis`] =
        `${prefix} select per hour, per day, or per booking.`;
    }
  });
  if (
    (serializeResourceDefaults(defaults, null)?.length ?? 0) > maxJsonLength
  ) {
    errors[''] = 'Resource defaults must be 1,048,576 characters or fewer.';
  }
  return errors;
}

export function createBillingRate(): BillingRate {
  return {
    amount: '',
    basis: 'hour',
    id: `rate_${crypto.randomUUID()}`,
    name: '',
  };
}

export function createOpeningHoursInterval(): OpeningHoursInterval {
  return { end: '17:00', start: '09:00' };
}
