import { useState } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FormBuilder } from '@/features/resource-templates/FormBuilder.tsx';
import { FormPreview } from '@/features/resource-templates/FormPreview.tsx';
import {
  parseFormDefinition,
  validateFormDefinition,
} from '@/features/resource-templates/formDefinition.ts';
import type { FormDefinition } from '@/features/resource-templates/models/FormDefinition.ts';

afterEach(cleanup);

describe('resource template form builder', () => {
  it('adds every supported field type and preserves labels, rules, and IDs when reordered', () => {
    const changed = vi.fn<(value: FormDefinition) => void>();
    function Builder() {
      const [definition, setDefinition] = useState<FormDefinition>({ fields: [] });
      return (
        <FormBuilder
          onChange={(value) => {
            changed(value);
            setDefinition(value);
          }}
          value={definition}
        />
      );
    }
    render(<Builder />);

    fireEvent.click(screen.getByRole('button', { name: 'Add Input' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Label' }), {
      target: { value: 'Visitor name' },
    });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Required' }));
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Maximum length' }), {
      target: { value: '40' },
    });
    const originalInput = changed.mock.lastCall![0].fields[0];

    for (const name of [
      'Add Text area',
      'Add Checkboxes',
      'Add Dropdown',
      'Add Radio buttons',
      'Add Text block',
    ]) {
      fireEvent.click(screen.getByRole('button', { name }));
    }
    fireEvent.change(screen.getByRole('textbox', { name: 'Text content' }), {
      target: { value: 'Please provide your booking details.' },
    });
    fireEvent.click(
      screen.getByRole('button', { name: 'Move Visitor name down' })
    );

    const saved = changed.mock.lastCall![0];
    expect(saved.fields.map((field) => field.type)).toEqual([
      'textarea',
      'input',
      'checkboxes',
      'dropdown',
      'radio',
      'text',
    ]);
    expect(saved.fields[1]).toEqual(originalInput);
    expect(saved.fields[1].validation).toMatchObject({
      maxLength: 40,
      required: true,
    });
    expect(saved.fields[5].label).toBe('Please provide your booking details.');
    expect(parseFormDefinition(JSON.stringify(saved), 1)).toEqual(saved);
  });

  it('requires at least one checkbox choice, without requiring every choice', async () => {
    render(
      <FormPreview
        definition={{
          fields: [
            {
              id: 'equipment',
              label: 'Equipment',
              options: [
                { id: 'camera', label: 'Camera' },
                { id: 'tripod', label: 'Tripod' },
              ],
              type: 'checkboxes',
              validation: { required: true },
            },
          ],
        }}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Try validation' }));
    expect(
      await screen.findByText('Select at least one option.')
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Tripod' }));
    fireEvent.click(screen.getByRole('button', { name: 'Try validation' }));
    expect(
      await screen.findByText(
        'Preview passes validation. No responses have been saved.'
      )
    ).toBeInTheDocument();
    expect(
      screen.queryByText('Select at least one option.')
    ).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Camera' })).not.toBeChecked();
  });

  it('lets an admin rename, add, and remove choices while preserving the retained choice IDs', () => {
    const changed = vi.fn<(value: FormDefinition) => void>();
    function Builder() {
      const [value, setValue] = useState<FormDefinition>({
        fields: [
          {
            id: 'location',
            label: 'Location',
            options: [
              { id: 'campus', label: 'Campus' },
              { id: 'other', label: 'Other' },
            ],
            type: 'dropdown',
          },
        ],
      });
      return (
        <FormBuilder
          onChange={(definition) => {
            changed(definition);
            setValue(definition);
          }}
          value={value}
        />
      );
    }
    render(<Builder />);

    fireEvent.change(screen.getByRole('textbox', { name: 'Choice 1' }), {
      target: { value: 'Main campus' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Add choice' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Choice 3' }), {
      target: { value: 'Remote' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Remove choice 2' }));

    expect(changed.mock.lastCall![0].fields[0].options).toEqual([
      { id: 'campus', label: 'Main campus' },
      { id: expect.any(String), label: 'Remote' },
    ]);
  });

  it('enforces text length and required choices in the interactive preview', async () => {
    render(
      <FormPreview
        definition={{
          fields: [
            {
              id: 'purpose',
              label: 'Purpose',
              type: 'textarea',
              validation: { maxLength: 8, minLength: 3, required: true },
            },
            {
              id: 'location',
              label: 'Location',
              options: [{ id: 'campus', label: 'Campus' }],
              type: 'dropdown',
              validation: { required: true },
            },
          ],
        }}
      />
    );

    fireEvent.change(screen.getByRole('textbox', { name: /Purpose/ }), {
      target: { value: 'Hi' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Try validation' }));
    expect(
      screen.queryByText(/Preview passes validation/)
    ).not.toBeInTheDocument();
    expect(await screen.findByText('This field is required.')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('combobox', { name: /Location/ }), {
      target: { value: 'campus' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Try validation' }));
    expect(
      await screen.findByText('Enter at least 3 characters.')
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Preview passes validation/)
    ).not.toBeInTheDocument();
    fireEvent.change(screen.getByRole('textbox', { name: /Purpose/ }), {
      target: { value: 'Photo' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Try validation' }));
    expect(await screen.findByText(/Preview passes validation/)).toBeInTheDocument();
    fireEvent.change(screen.getByRole('textbox', { name: /Purpose/ }), {
      target: { value: 'Too many characters' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Try validation' }));
    expect(
      await screen.findByText('Use 8 characters or fewer.')
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Preview passes validation/)
    ).not.toBeInTheDocument();
  });

  it.each([
    {
      description: 'IDs containing a trailing newline',
      field: { id: 'field\n', label: 'Name', type: 'input' },
    },
    {
      description: 'choice fields without choices',
      field: { id: 'field', label: 'Location', options: [], type: 'dropdown' },
    },
    {
      description: 'duplicate choice IDs',
      field: {
        id: 'field',
        label: 'Equipment',
        options: [
          { id: 'same', label: 'Camera' },
          { id: 'same', label: 'Tripod' },
        ],
        type: 'checkboxes',
      },
    },
    {
      description: 'minimum length greater than maximum length',
      field: {
        id: 'field',
        label: 'Name',
        type: 'input',
        validation: { maxLength: 4, minLength: 10 },
      },
    },
    {
      description: 'length rules on a radio group',
      field: {
        id: 'field',
        label: 'Location',
        options: [{ id: 'campus', label: 'Campus' }],
        type: 'radio',
        validation: { maxLength: 10 },
      },
    },
  ])('rejects $description before saving', ({ field }) => {
    expect(validateFormDefinition({ fields: [field] })).not.toEqual([]);
    expect(() =>
      parseFormDefinition(JSON.stringify({ fields: [field] }), 1)
    ).toThrow();
  });

  it('rejects unknown schema data instead of silently removing it on reload', () => {
    expect(() =>
      parseFormDefinition('{"fields":[],"conditionalRules":[]}', 1)
    ).toThrow();
    expect(() => parseFormDefinition('{"fields":[]}', 2)).toThrow(/version 2/);
  });

  it.each([
    '{"fields":[],"fields":[]}',
    '{"fields":[],"\\u0066ields":[]}',
    '{"fields":[{"id":"name","type":"input","label":"First","label":"Second"}]}',
  ])(
    'rejects duplicate JSON properties without silently discarding saved data: %s',
    (json) => {
      expect(() => parseFormDefinition(json, 1)).toThrow(/duplicate properties/);
    }
  );

  it('preserves quotes and JSON punctuation inside text labels', () => {
    const value: FormDefinition = {
      fields: [
        {
          id: 'instructions',
          label:
            'Include "fields": [] and {"label": "example"} in your explanation. 🌲',
          type: 'text',
        },
      ],
    };
    expect(parseFormDefinition(JSON.stringify(value), 1)).toEqual(value);
  });
});
