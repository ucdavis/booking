import { createFileRoute } from '@tanstack/react-router';
import { ResourceTemplateEditor } from '@/features/resource-templates/ResourceTemplateEditor.tsx';

export const Route = createFileRoute('/(authenticated)/teams/$teamSlug/resource-templates/new')({
  component: NewTeamResourceTemplatePage,
});

function NewTeamResourceTemplatePage() {
  const { teamSlug } = Route.useParams();
  const navigate = Route.useNavigate();
  return <ResourceTemplateEditor definition={{ fields: [] }} key={teamSlug} onSaved={async (template) => {
    await navigate({ params: { teamSlug, templateId: String(template.id) }, replace: true, to: '/teams/$teamSlug/resource-templates/$templateId' });
  }} teamSlug={teamSlug} />;
}
