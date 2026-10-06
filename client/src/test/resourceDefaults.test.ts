import { describe, expect, it } from 'vitest';
import {
  createBillingRate,
  createOpeningHoursInterval,
  parseResourceDefaultsJson,
  serializeResourceDefaults,
  validateResourceDefaults,
} from '@/features/resource-defaults/resourceDefaults.ts';
import type { ResourceDefaults } from '@/features/resource-defaults/models/ResourceDefaults.ts';

const rate = {
  amount: '25.00',
  basis: 'hour',
  id: 'standard',
  name: 'Standard',
} as const;

describe('resource defaults JSON', () => {
  it.each([undefined, null, '', '  \n'])(
    'accepts absent defaults: %j',
    (source) => {
      expect(parseResourceDefaultsJson(source)).toEqual({ defaults: {} });
      expect(serializeResourceDefaults({}, source)).toBeNull();
    }
  );

  it('preserves exact unmodified source, including unversioned and unknown values', () => {
    const source =
      '{\n "capacity": 12, "openingHours": {"monday": []}, "future": [true, null, {"mode":"x"}]\n}';
    const parsed = parseResourceDefaultsJson(source);
    expect(parsed.unavailableReason).toBeUndefined();
    expect(serializeResourceDefaults(parsed.defaults, source)).toBe(source);
    expect(
      serializeResourceDefaults(
        {
          capacity: 12,
          future: parsed.defaults.future,
          openingHours: parsed.defaults.openingHours,
        },
        source
      )
    ).toBe(source);
  });

  it('preserves unknown properties while editing hours and precise decimal amounts', () => {
    const source = JSON.stringify({
      billingRates: [
        { ...rate, currency: 'USD', futurePricePolicy: { mode: 'x' } },
      ],
      capacity: 12,
      openingHours: {
        holidayPolicy: { mode: 'inherit' },
        monday: [{ end: '17:00', futureFlag: true, start: '09:00' }],
      },
      schemaVersion: 1,
    });
    const { defaults } = parseResourceDefaultsJson(source);
    const edited: ResourceDefaults = {
      ...defaults,
      billingRates: defaults.billingRates!.map((current) => ({
        ...current,
        amount: '0.12345678901234567890',
      })),
      openingHours: {
        ...defaults.openingHours,
        monday: defaults.openingHours!.monday!.map((interval) => ({
          ...interval,
          start: '08:00',
        })),
      },
    };
    const saved = JSON.parse(serializeResourceDefaults(edited, source)!);
    expect(saved.capacity).toBe(12);
    expect(saved.openingHours.monday[0]).toEqual({
      end: '17:00',
      futureFlag: true,
      start: '08:00',
    });
    expect(saved.openingHours.holidayPolicy).toEqual({ mode: 'inherit' });
    expect(saved.billingRates[0]).toMatchObject({
      amount: '0.12345678901234567890',
      currency: 'USD',
      futurePricePolicy: { mode: 'x' },
    });
    expect(validateResourceDefaults(edited)).toEqual([]);
  });

  it('writes a version only when settings are edited and distinguishes unspecified from closed', () => {
    expect(
      JSON.parse(
        serializeResourceDefaults({ openingHours: { monday: [] } }, null)!
      )
    ).toEqual({
      openingHours: { monday: [] },
      schemaVersion: 1,
    });
  });

  it.each([
    { openingHours: {} },
    { billingRates: [] },
    { billingRates: [], openingHours: {} },
  ])(
    'preserves explicitly included empty sections through saving and later edits: %j',
    (defaults) => {
      const source = serializeResourceDefaults(defaults, null)!;
      const parsed = parseResourceDefaultsJson(source);
      expect(parsed.unavailableReason).toBeUndefined();
      expect(parsed.defaults).toEqual({ ...defaults, schemaVersion: 1 });
      expect(serializeResourceDefaults(parsed.defaults, source)).toBe(source);
      expect(
        JSON.parse(
          serializeResourceDefaults(
            { ...parsed.defaults, capacity: 12 },
            source
          )!
        )
      ).toEqual({
        ...defaults,
        capacity: 12,
        schemaVersion: 1,
      });
    }
  );

  it('clears the final settings to null while preserving unknown root data', () => {
    const source = JSON.stringify({
      billingRates: [rate],
      openingHours: { monday: [] },
      schemaVersion: 1,
    });
    const { defaults } = parseResourceDefaultsJson(source);
    delete defaults.billingRates;
    delete defaults.openingHours;
    expect(serializeResourceDefaults(defaults, source)).toBeNull();
    const withUnknown = JSON.stringify({
      billingRates: [rate],
      capacity: 12,
      schemaVersion: 1,
    });
    expect(
      JSON.parse(
        serializeResourceDefaults(
          { capacity: 12, schemaVersion: 1 },
          withUnknown
        )!
      )
    ).toEqual({ capacity: 12, schemaVersion: 1 });
  });

  it.each([
    '{invalid}',
    '[]',
    'null',
    '{"schemaVersion":2,"openingHours":{}}',
    '{"schemaVersion":null}',
    '{"openingHours":null}',
    '{"openingHours":{"monday":"closed"}}',
    '{"openingHours":{"monday":[{"start":9,"end":"17:00"}]}}',
    '{"billingRates":{"hourly":25}}',
    JSON.stringify({ billingRates: [{ ...rate, amount: 25 }] }),
    JSON.stringify({ billingRates: [{ ...rate, basis: 'week' }] }),
    JSON.stringify({ billingRates: [{ ...rate, currency: 'EUR' }] }),
    JSON.stringify({ billingRates: [{ ...rate, currency: null }] }),
    JSON.stringify({ billingRates: [{ ...rate, id: '' }] }),
    JSON.stringify({ billingRates: [rate, rate] }),
  ])(
    'protects unsupported or incompatible data from replacement: %s',
    (source) => {
      const parsed = parseResourceDefaultsJson(source);
      expect(parsed.defaults).toEqual({});
      expect(parsed.unavailableReason).toBeTruthy();
      expect(
        serializeResourceDefaults({ openingHours: { monday: [] } }, source)
      ).toBe(source);
    }
  );

  it.each([
    '{"x":1,"x":2}',
    String.raw`{"nested":{"x":1,"\u0078":2}}`,
    '{"x":9007199254740993}',
    '{"x":0.12345678901234567890}',
    '{"x":1e400}',
    '{"x":1e-400}',
    '{"x":-0}',
  ])(
    'protects duplicate keys and numeric values that would lose information: %s',
    (source) => {
      expect(parseResourceDefaultsJson(source).unavailableReason).toBeTruthy();
      expect(serializeResourceDefaults({}, source)).toBe(source);
    }
  );

  it.each([
    '{"x":1e2,"y":0.10,"z":1.2300e-3}',
    '{"x":9007199254740992}',
    '{"x":0e9999999999999999999999999}',
    '{"left":{"x":1},"right":{"x":2}}',
    JSON.stringify({
      array: [{ x: 1 }, { x: 2 }],
      x: '9007199254740993, "x": 1',
    }),
  ])(
    'allows lossless numeric forms and repeated keys in separate objects: %s',
    (source) => {
      const parsed = parseResourceDefaultsJson(source);
      expect(parsed.unavailableReason).toBeUndefined();
      expect(serializeResourceDefaults(parsed.defaults, source)).toBe(source);
    }
  );

  it('protects documents beyond the server JSON depth limit', () => {
    const supported = `{"future":${'['.repeat(63)}0${']'.repeat(63)}}`;
    const unsupported = `{"future":${'['.repeat(64)}0${']'.repeat(64)}}`;
    expect(
      parseResourceDefaultsJson(supported).unavailableReason
    ).toBeUndefined();
    expect(
      parseResourceDefaultsJson(unsupported).unavailableReason
    ).toBeTruthy();
    expect(serializeResourceDefaults({}, unsupported)).toBe(unsupported);
  });

  it('protects oversized existing documents and validates oversized new settings', () => {
    const defaults = { future: 'x'.repeat(1024 * 1024) };
    const source = JSON.stringify(defaults);
    expect(parseResourceDefaultsJson(source).unavailableReason).toMatch(/size/);
    expect(validateResourceDefaults(defaults)).toEqual([
      'Resource defaults must be 1,048,576 characters or fewer.',
    ]);
    expect(serializeResourceDefaults({}, source)).toBe(source);
  });
});

describe('opening hours validation', () => {
  it('accepts optional, closed, split and adjacent same-day periods', () => {
    expect(validateResourceDefaults({})).toEqual([]);
    expect(validateResourceDefaults({ openingHours: {} })).toEqual([]);
    expect(
      validateResourceDefaults({
        openingHours: {
          friday: [{ end: '23:59', start: '22:00' }],
          monday: [],
          saturday: [{ end: '02:00', start: '00:00' }],
          tuesday: [
            { end: '12:00', start: '09:00' },
            { end: '17:00', start: '12:00' },
          ],
        },
      })
    ).toEqual([]);
  });

  it.each([
    { end: '17:00', start: '' },
    { end: '17:00', start: '9:00' },
    { end: '17:00', start: '09:60' },
    { end: '24:00', start: '09:00' },
    { end: '09:00', start: '09:00' },
    { end: '02:00', start: '22:00' },
  ])('rejects invalid or ambiguous intervals: %j', (interval) => {
    expect(
      validateResourceDefaults({ openingHours: { monday: [interval] } })
    ).not.toEqual([]);
  });

  it('rejects overlaps within a day while allowing the same hours on different days', () => {
    expect(
      validateResourceDefaults({
        openingHours: {
          monday: [
            { end: '17:00', start: '11:00' },
            { end: '12:00', start: '09:00' },
          ],
        },
      })
    ).toEqual(['Monday: opening hours must not overlap.']);
    expect(
      validateResourceDefaults({
        openingHours: {
          monday: [{ end: '17:00', start: '09:00' }],
          sunday: [{ end: '17:00', start: '09:00' }],
        },
      })
    ).toEqual([]);
  });
});

describe('billing rate validation', () => {
  it.each([
    '0',
    '0.00',
    '25',
    '25.50',
    '0.00000000001',
    '12345678901234567890.123456789',
  ])(
    'preserves valid decimal amounts without floating-point rounding: %s',
    (amount) => {
      expect(
        validateResourceDefaults({ billingRates: [{ ...rate, amount }] })
      ).toEqual([]);
    }
  );

  it.each([
    '',
    '-1',
    '+1',
    '.5',
    '1.',
    '1e2',
    'NaN',
    'Infinity',
    '1,234',
    ' 25 ',
  ])('rejects incomplete or non-decimal amounts: %s', (amount) => {
    expect(
      validateResourceDefaults({ billingRates: [{ ...rate, amount }] })
    ).toEqual([expect.stringMatching(/nonnegative amount/)]);
  });

  it('requires names and unique rate identities without changing non-USD amounts', () => {
    expect(
      validateResourceDefaults({
        billingRates: [{ ...rate, currency: 'usd', name: '' }],
      })
    ).toEqual([
      expect.stringMatching(/name/),
      expect.stringMatching(/US dollar/),
    ]);
    expect(
      validateResourceDefaults({
        billingRates: [rate, { ...rate, name: 'Second' }],
      })
    ).toEqual([expect.stringMatching(/unique ID/)]);
    expect(
      validateResourceDefaults({
        billingRates: [{ ...rate, name: 'x'.repeat(101) }],
      })
    ).toEqual([expect.stringMatching(/name/)]);
  });

  it('creates independent editable defaults without inventing a price', () => {
    const first = createBillingRate();
    const second = createBillingRate();
    expect(first).toMatchObject({
      amount: '',
      basis: 'hour',
      name: '',
    });
    expect(first.id).not.toBe(second.id);
    expect(first).not.toHaveProperty('currency');
    expect(createOpeningHoursInterval()).toEqual({
      end: '17:00',
      start: '09:00',
    });
  });
});
