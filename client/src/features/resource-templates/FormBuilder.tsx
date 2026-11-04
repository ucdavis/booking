import { ChevronDownIcon, ChevronUpIcon } from '@heroicons/react/24/outline';
import { useState } from 'react';
import { FormFieldEditor } from './FormFieldEditor.tsx';
import { createField, fieldTypeLabels } from './formDefinition.ts';
import type { FormDefinition } from './models/FormDefinition.ts';
import type { FormField } from './models/FormField.ts';

const fieldTypes: FormField['type'][] = [
  'input',
  'textarea',
  'checkboxes',
  'dropdown',
  'radio',
  'text',
];

export function FormBuilder({
  disabled = false,
  embedded = false,
  onChange,
  value,
}: {
  disabled?: boolean;
  embedded?: boolean;
  onChange: (value: FormDefinition) => void;
  value: FormDefinition;
}) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [draggedId, setDraggedId] = useState<string | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const selectedField =
    value.fields.find((field) => field.id === selectedId) ?? value.fields[0];

  const moveField = (id: string, targetIndex: number) => {
    const currentIndex = value.fields.findIndex((field) => field.id === id);
    if (
      disabled ||
      currentIndex < 0 ||
      targetIndex < 0 ||
      targetIndex >= value.fields.length ||
      currentIndex === targetIndex
    ) {
      return;
    }
    const fields = [...value.fields];
    const [field] = fields.splice(currentIndex, 1);
    fields.splice(targetIndex, 0, field);
    onChange({ fields });
    setAnnouncement(
      `${field.label || fieldTypeLabels[field.type]} moved to position ${targetIndex + 1}.`
    );
  };

  return (
    <section aria-label="Form builder" className="space-y-5">
      <div>
        <h2 className="text-xl font-semibold text-primary">Build your form</h2>
        <p className="mt-1 text-sm text-base-content/75">
          Choose a field to edit its label, choices, and requirements. The list
          shows the order people will see when making a reservation.
        </p>
      </div>
      <div>
        <h3 className="mb-3 text-sm font-semibold">Add a field</h3>
        <div aria-label="Add a form field" className="flex flex-wrap gap-2">
          {fieldTypes.map((type) => (
            <button
              className="btn btn-outline btn-sm"
              disabled={disabled || value.fields.length >= 100}
              key={type}
              onClick={() => {
                const field = createField(type);
                onChange({ fields: [...value.fields, field] });
                setSelectedId(field.id);
                setAnnouncement(`${fieldTypeLabels[type]} added.`);
              }}
              type="button"
            >
              Add {fieldTypeLabels[type]}
            </button>
          ))}
        </div>
      </div>
      {value.fields.length >= 100 && (
        <p className="text-sm text-base-content/75">
          This form has reached the limit of 100 fields.
        </p>
      )}
      {!value.fields.length ? (
        <div className="rounded-sm border border-dashed border-base-300 p-8 text-center text-base-content/75">
          Your form is empty. Add a field or a text block to get started.
        </div>
      ) : (
        <div className="grid items-start gap-5 lg:grid-cols-[20rem_minmax(0,1fr)]">
          <div className="space-y-4">
            <div className="flex items-center justify-between gap-3">
              <h3 className="font-semibold text-primary">Form fields</h3>
              <span className="text-sm text-base-content/75">
                {value.fields.length} / 100
              </span>
            </div>
            <ol aria-label="Form fields" className="space-y-2">
              {value.fields.map((field, index) => (
                <li
                  className={`rounded-sm border p-3 ${selectedField?.id === field.id ? 'border-primary bg-primary/5' : 'border-base-300 bg-base-100'}`}
                  key={field.id}
                  onDragOver={(event) => {
                    if (!disabled && draggedId) {
                      event.preventDefault();
                      event.dataTransfer.dropEffect = 'move';
                    }
                  }}
                  onDrop={(event) => {
                    event.preventDefault();
                    if (draggedId) {
                      moveField(draggedId, index);
                    }
                    setDraggedId(null);
                  }}
                >
                  <div className="flex items-start gap-2">
                    <button
                      aria-label={`Drag ${field.label || fieldTypeLabels[field.type]} to reorder`}
                      className="btn btn-ghost btn-sm cursor-grab"
                      disabled={disabled}
                      draggable={!disabled}
                      onDragEnd={() => setDraggedId(null)}
                      onDragStart={(event) => {
                        setDraggedId(field.id);
                        event.dataTransfer.setData('text/plain', field.id);
                        event.dataTransfer.effectAllowed = 'move';
                      }}
                      tabIndex={-1}
                      type="button"
                    >
                      <span aria-hidden="true">⠿</span>
                    </button>
                    <button
                      aria-pressed={selectedField?.id === field.id}
                      className="min-w-0 flex-1 rounded text-left focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary"
                      disabled={disabled}
                      onClick={() => setSelectedId(field.id)}
                      type="button"
                    >
                      <span className="flex items-baseline justify-between gap-2 text-xs text-base-content/60">
                        <span className="font-semibold tracking-wide">
                          {index + 1}. {fieldTypeLabels[field.type]}
                        </span>
                        {field.validation?.required && (
                          <span className="shrink-0">Required</span>
                        )}
                      </span>
                      <span className="mt-1 block truncate font-medium">
                        {field.label || 'Untitled field'}
                      </span>
                    </button>
                  </div>
                  <div className="mt-2 flex items-center gap-1">
                    <button
                      aria-label={`Move ${field.label} up`}
                      className="btn btn-ghost btn-square btn-xs"
                      disabled={disabled || index === 0}
                      onClick={() => moveField(field.id, index - 1)}
                      type="button"
                    >
                      <ChevronUpIcon aria-hidden="true" className="size-4" />
                    </button>
                    <button
                      aria-label={`Move ${field.label} down`}
                      className="btn btn-ghost btn-square btn-xs"
                      disabled={disabled || index === value.fields.length - 1}
                      onClick={() => moveField(field.id, index + 1)}
                      type="button"
                    >
                      <ChevronDownIcon aria-hidden="true" className="size-4" />
                    </button>
                    <button
                      aria-label={`Remove ${field.label || fieldTypeLabels[field.type]}`}
                      className="btn btn-ghost btn-xs ml-auto text-error"
                      disabled={disabled}
                      onClick={() => {
                        onChange({
                          fields: value.fields.filter(
                            (current) => current.id !== field.id
                          ),
                        });
                        setAnnouncement(
                          `${field.label || fieldTypeLabels[field.type]} removed.`
                        );
                      }}
                      type="button"
                    >
                      Remove
                    </button>
                  </div>
                </li>
              ))}
            </ol>
          </div>
          {selectedField && (
            <div
              className={
                embedded
                  ? 'min-w-0 rounded-sm border border-base-300 bg-base-100 p-4 sm:p-6'
                  : 'rounded-sm border border-base-300 bg-base-100 p-5 shadow-sm'
              }
            >
              <FormFieldEditor
                disabled={disabled}
                field={selectedField}
                key={selectedField.id}
                onChange={(updated) =>
                  onChange({
                    fields: value.fields.map((field) =>
                      field.id === updated.id ? updated : field
                    ),
                  })
                }
              />
            </div>
          )}
        </div>
      )}
      <p aria-live="polite" className="sr-only">
        {announcement}
      </p>
    </section>
  );
}
