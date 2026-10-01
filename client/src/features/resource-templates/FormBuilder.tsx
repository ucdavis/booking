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
  onChange,
  value,
}: {
  disabled?: boolean;
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
        <p className="mt-1 text-sm text-base-content/65">
          Add fields, then select one to edit its settings. Drag a field handle
          or use the move buttons to change the order.
        </p>
      </div>
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
      {value.fields.length >= 100 && (
        <p className="text-sm text-base-content/65">
          This form has reached the limit of 100 fields.
        </p>
      )}
      {!value.fields.length ? (
        <div className="rounded-xl border border-dashed border-base-300 p-8 text-center text-base-content/65">
          Your form is empty. Add a field or a text block to get started.
        </div>
      ) : (
        <div className="grid items-start gap-5 xl:grid-cols-2">
          <ol aria-label="Form fields" className="space-y-3">
            {value.fields.map((field, index) => (
              <li
                className={`rounded-xl border p-4 ${selectedField?.id === field.id ? 'border-primary bg-primary/5' : 'border-base-300 bg-base-100'}`}
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
                    <span className="block text-xs font-semibold tracking-wide text-base-content/60">
                      {index + 1}. {fieldTypeLabels[field.type]}
                    </span>
                    <span className="mt-1 block truncate font-medium">
                      {field.label || 'Untitled field'}
                    </span>
                    {field.validation?.required && (
                      <span className="mt-1 block text-xs text-base-content/60">
                        Required
                      </span>
                    )}
                  </button>
                </div>
                <div className="mt-3 flex flex-wrap gap-1">
                  <button
                    aria-label={`Move ${field.label} up`}
                    className="btn btn-ghost btn-xs"
                    disabled={disabled || index === 0}
                    onClick={() => moveField(field.id, index - 1)}
                    type="button"
                  >
                    Move up
                  </button>
                  <button
                    aria-label={`Move ${field.label} down`}
                    className="btn btn-ghost btn-xs"
                    disabled={disabled || index === value.fields.length - 1}
                    onClick={() => moveField(field.id, index + 1)}
                    type="button"
                  >
                    Move down
                  </button>
                  <button
                    aria-label={`Remove ${field.label || fieldTypeLabels[field.type]}`}
                    className="btn btn-ghost btn-xs text-error"
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
          {selectedField && (
            <div className="rounded-xl border border-base-300 bg-base-100 p-5">
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
