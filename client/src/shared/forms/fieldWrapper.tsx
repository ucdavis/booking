import { ReactNode } from 'react';
import { useFieldContext } from './formContext.tsx';

interface FieldWrapperProps {
  children: ReactNode;
  controlId: string;
  label: string;
}

/**
 * Common wrapper component for form fields that handles label, error display, and validation state
 */
export function FieldWrapper({
  children,
  controlId,
  label,
}: FieldWrapperProps) {
  const field = useFieldContext<string>();
  const hasError = field.state.meta.isTouched && !field.state.meta.isValid;

  return (
    <div className="form-control w-full">
      <label
        className="mb-2 block text-sm font-semibold text-base-content"
        htmlFor={controlId}
      >
        {label}
      </label>
      {children}
      {hasError && (
        <p
          className="mt-2 text-sm text-error"
          id={`${controlId}-error`}
          role="alert"
        >
          {field.state.meta.errors
            .map((error) =>
              typeof error === 'string' ? error : error?.message
            )
            .filter(Boolean)
            .join(', ')}
        </p>
      )}
      {field.state.meta.isValidating && (
        <span className="loading loading-spinner loading-xs ml-2"></span>
      )}
    </div>
  );
}
