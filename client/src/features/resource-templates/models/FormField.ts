import type { FormOption } from './FormOption.ts';
import type { FormValidation } from './FormValidation.ts';

export interface FormField {
  helpText?: string;
  id: string;
  label: string;
  options?: FormOption[];
  type: 'input' | 'textarea' | 'checkboxes' | 'dropdown' | 'radio' | 'text';
  validation?: FormValidation;
}
