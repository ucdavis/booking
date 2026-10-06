import { BillingRatesEditor } from './BillingRatesEditor.tsx';
import { OpeningHoursEditor } from './OpeningHoursEditor.tsx';
import { resourceDefaultsFormValues } from './resourceDefaults.ts';
import { withFieldGroup } from '@/shared/forms/formContext.tsx';

export const ResourceDefaultsEditor = withFieldGroup({
  defaultValues: resourceDefaultsFormValues({}),
  props: {
    disabled: false,
    readOnly: false,
    unavailableReason: undefined as string | undefined,
  },
  render: function ResourceDefaultsEditor({
    disabled,
    group,
    readOnly,
    unavailableReason,
  }) {
    return (
      <section
        aria-label="Resource defaults"
        className="space-y-5 rounded-xl border border-base-300 bg-base-100 p-5"
      >
        <div>
          <h2 className="text-xl font-semibold text-primary">
            Resource defaults
          </h2>
          <p className="mt-2 text-sm text-base-content/65">
            Optional starting values for resources. These defaults are saved
            with the template and included when it is duplicated.
          </p>
        </div>
        {unavailableReason ? (
          <div
            className="space-y-2 rounded-lg border border-warning bg-warning/10 p-4"
            role="status"
          >
            <p>
              These saved defaults use a format this editor cannot change. They
              will be preserved when you save other template changes.
            </p>
            <p className="text-sm">{unavailableReason}</p>
          </div>
        ) : (
          <div className="space-y-6">
            <OpeningHoursEditor
              disabled={disabled}
              fields={{ openingHours: 'openingHours' }}
              form={group}
              readOnly={readOnly}
            />
            <BillingRatesEditor
              disabled={disabled}
              fields={{ billingRates: 'billingRates' }}
              form={group}
              readOnly={readOnly}
            />
          </div>
        )}
      </section>
    );
  },
});
