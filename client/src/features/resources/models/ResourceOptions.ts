import type { ResourceSpace } from './ResourceSpace.ts';
import type { ResourceTemplate } from '@/features/resource-templates/models/ResourceTemplate.ts';

export interface ResourceOptions {
  spaces: ResourceSpace[];
  templates: ResourceTemplate[];
}
