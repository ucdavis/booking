import { createFileRoute } from '@tanstack/react-router';
import { ResourceTemplatesPage } from '@/features/resource-templates/ResourceTemplatesPage.tsx';

export const Route = createFileRoute('/(authenticated)/admin/resource-templates/')({
  component: ResourceTemplatesPage,
});
