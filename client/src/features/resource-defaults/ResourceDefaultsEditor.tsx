import { BillingRatesEditor } from './BillingRatesEditor.tsx';
import { OpeningHoursEditor } from './OpeningHoursEditor.tsx';
import type { ResourceDefaults } from './models/ResourceDefaults.ts';

export function ResourceDefaultsEditor({
  disabled = false,
  errors = [],
  onChange,
  readOnly = false,
  unavailableReason,
  value,
}: {
  disabled?: boolean;
  errors?: string[];
  onChange: (value: ResourceDefaults) => void;
  readOnly?: boolean;
  unavailableReason?: string;
  value: ResourceDefaults;
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
          Optional starting values for resources. These defaults are saved with
          the template and included when it is duplicated.
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
            onChange={onChange}
            readOnly={readOnly}
            value={value}
          />
          <BillingRatesEditor
            disabled={disabled}
            onChange={onChange}
            readOnly={readOnly}
            value={value}
          />
        </div>
      )}
      {errors.length > 0 && (
        <div
          className="rounded-lg border border-error/30 bg-error/5 p-4 text-sm text-error"
          role="alert"
        >
          <p className="font-semibold">Review these resource defaults:</p>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            {errors.map((error) => (
              <li key={error}>{error}</li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}
