import { BillingRatesEditor } from './BillingRatesEditor.tsx';
import { OpeningHoursEditor } from './OpeningHoursEditor.tsx';
import { resourceDefaultsFormValues } from './resourceDefaults.ts';
import { withFieldGroup } from '@/shared/forms/formContext.tsx';

export const ResourceDefaultsEditor = withFieldGroup({
  defaultValues: resourceDefaultsFormValues({}),
  props: {
    context: 'template' as 'template' | 'resource',
    disabled: false,
    embedded: false,
    readOnly: false,
    unavailableReason: undefined as string | undefined,
  },
  render: function ResourceDefaultsEditor({
    context,
    disabled,
    embedded,
    group,
    readOnly,
    unavailableReason,
  }) {
    return (
      <section
        aria-label={
          context === 'resource' ? 'Hours and rates' : 'Resource defaults'
        }
        className={
          embedded
            ? 'space-y-5'
            : 'space-y-5 rounded-sm border border-base-300 bg-base-100 p-5'
        }
      >
        <div>
          <h2 className="text-xl font-semibold text-primary">
            {context === 'resource' ? 'Hours and rates' : 'Resource defaults'}
          </h2>
          <p className="mt-2 text-sm text-base-content/65">
            {context === 'resource'
              ? 'Opening hours and billing rates for this resource. Changes are saved with its configuration.'
              : 'Optional starting values for resources. These defaults are saved with the template and included when it is duplicated.'}
          </p>
        </div>
        {unavailableReason ? (
          <div
            className="space-y-2 rounded-sm border border-warning bg-warning/10 p-4"
            role="status"
          >
            <p>
              These saved defaults use a format this editor cannot change. They
              will be preserved when you save other {context} changes.
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
