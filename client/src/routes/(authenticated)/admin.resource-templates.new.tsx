import { createFileRoute } from '@tanstack/react-router';
import { ResourceTemplateEditor } from '@/features/resource-templates/ResourceTemplateEditor.tsx';

export const Route = createFileRoute('/(authenticated)/admin/resource-templates/new')({
  component: NewResourceTemplatePage,
});

function NewResourceTemplatePage() {
  const navigate = Route.useNavigate();
  return <ResourceTemplateEditor definition={{ fields: [] }} onSaved={async (template) => {
    await navigate({ params: { templateId: String(template.id) }, replace: true, to: '/admin/resource-templates/$templateId' });
  }} />;
}
