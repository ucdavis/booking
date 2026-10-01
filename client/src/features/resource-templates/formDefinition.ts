import { z } from 'zod';
import type { FormDefinition } from './models/FormDefinition.ts';
import type { FormField } from './models/FormField.ts';

const maxJsonLength = 1024 * 1024;

export const fieldTypeLabels: Record<FormField['type'], string> = {
  checkboxes: 'Checkboxes',
  dropdown: 'Dropdown',
  input: 'Input',
  radio: 'Radio buttons',
  text: 'Text block',
  textarea: 'Text area',
};

const idSchema = z
  .string()
  .min(1)
  .max(100)
  .refine((value) => !/[^\w-]/.test(value), 'Use a valid field or option ID.');
const labelSchema = z
  .string()
  .max(10_000, 'Use 10,000 characters or fewer for labels and text.')
  .refine((value) => value.trim().length > 0, 'Enter a label or text.');
const validationSchema = z.strictObject({
  maxLength: z.number().int().min(1).max(100_000).optional(),
  minLength: z.number().int().min(1).max(100_000).optional(),
  required: z.boolean().optional(),
});
const fieldSchema = z
  .strictObject({
    helpText: z.string().max(2000).optional(),
    id: idSchema,
    label: labelSchema,
    options: z
      .array(
        z.strictObject({
          id: idSchema,
          label: z
            .string()
            .max(200)
            .refine(
              (value) => value.trim().length > 0,
              'Enter a choice label.'
            ),
        })
      )
      .max(100, 'Use at most 100 choices per field.')
      .optional(),
    type: z.enum([
      'input',
      'textarea',
      'checkboxes',
      'dropdown',
      'radio',
      'text',
    ]),
    validation: validationSchema.optional(),
  })
  .superRefine((field, context) => {
    const isChoice = ['checkboxes', 'dropdown', 'radio'].includes(field.type);
    const isTextInput = field.type === 'input' || field.type === 'textarea';
    if (isChoice && !field.options?.length) {
      context.addIssue({ code: 'custom', message: 'Add at least one choice.' });
    }
    if (!isChoice && field.options !== undefined) {
      context.addIssue({
        code: 'custom',
        message: 'Only choice fields may contain choices.',
      });
    }
    if (field.type === 'text' && field.validation !== undefined) {
      context.addIssue({
        code: 'custom',
        message: 'Text blocks cannot contain validation rules.',
      });
    }
    if (
      !isTextInput &&
      (field.validation?.minLength !== undefined ||
        field.validation?.maxLength !== undefined)
    ) {
      context.addIssue({
        code: 'custom',
        message:
          'Length rules are only available for input and text area fields.',
      });
    }
    if (
      field.validation?.minLength !== undefined &&
      field.validation.maxLength !== undefined &&
      field.validation.minLength > field.validation.maxLength
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Minimum length cannot exceed maximum length.',
      });
    }
    if (
      field.options &&
      new Set(field.options.map((option) => option.id)).size !==
        field.options.length
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Each choice in a field must have a unique ID.',
      });
    }
  });
const definitionSchema = z
  .strictObject({
    fields: z.array(fieldSchema).max(100, 'Use at most 100 fields.'),
  })
  .superRefine((definition, context) => {
    if (
      new Set(definition.fields.map((field) => field.id)).size !==
      definition.fields.length
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Each field must have a unique ID.',
      });
    }
  });

export function createField(type: FormField['type']): FormField {
  const field: FormField = {
    id: `field_${crypto.randomUUID()}`,
    label:
      type === 'text'
        ? 'Add your text here.'
        : `New ${fieldTypeLabels[type].toLowerCase()}`,
    type,
  };
  if (['checkboxes', 'dropdown', 'radio'].includes(type)) {
    field.options = [
      { id: `option_${crypto.randomUUID()}`, label: 'Option 1' },
      { id: `option_${crypto.randomUUID()}`, label: 'Option 2' },
    ];
  }
  return field;
}

export function validateFormDefinition(value: unknown): string[] {
  const result = definitionSchema.safeParse(value);
  if (result.success) {
    return JSON.stringify(result.data).length > maxJsonLength
      ? ['The form definition must be no larger than 1 MiB of text.']
      : [];
  }
  return result.error.issues.map((issue) => {
    const fieldIndex = issue.path[0] === 'fields' ? issue.path[1] : undefined;
    const prefix =
      typeof fieldIndex === 'number' ? `Field ${fieldIndex + 1}: ` : '';
    return `${prefix}${issue.message}`;
  });
}

export function parseFormDefinition(
  json: string,
  version: number
): FormDefinition {
  if (version !== 1) {
    throw new Error(
      `Form schema version ${version} is not supported by this editor.`
    );
  }
  if (json.length > maxJsonLength) {
    throw new Error('The saved form exceeds the size supported by this editor.');
  }
  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch {
    throw new Error(
      'The saved form is not valid JSON. It cannot be edited safely.'
    );
  }
  // JSON.parse has checked the syntax. Scan complete string tokens so duplicate
  // property names are rejected without treating punctuation in text as JSON.
  const propertyNames: (Set<string> | null)[] = [];
  for (const token of json.matchAll(/("(?:\\.|[^"\\])*")(\s*:)?|([[\]{}])/g)) {
    if (token[3] === '{') {
      propertyNames.push(new Set());
    } else if (token[3] === '[') {
      propertyNames.push(null);
    } else if (token[3] === '}' || token[3] === ']') {
      propertyNames.pop();
    } else if (token[2]) {
      const names = propertyNames.at(-1);
      const name: string = JSON.parse(token[1]);
      if (names?.has(name)) {
        throw new Error(
          'The saved form contains duplicate properties and cannot be edited safely.'
        );
      }
      names?.add(name);
    }
  }
  const errors = validateFormDefinition(value);
  if (errors.length) {
    throw new Error(
      `The saved form cannot be edited safely. ${errors.join(' ')}`
    );
  }
  return definitionSchema.parse(value);
}
