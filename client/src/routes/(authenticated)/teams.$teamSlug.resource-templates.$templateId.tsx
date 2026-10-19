import { createFileRoute } from '@tanstack/react-router';
import { ResourceTemplatePage } from '@/features/resource-templates/ResourceTemplatePage.tsx';

export const Route = createFileRoute('/(authenticated)/teams/$teamSlug/resource-templates/$templateId')({
  component: TeamResourceTemplatePage,
  validateSearch: (search): { view?: boolean } => search.view === true ? { view: true } : {},
});

function TeamResourceTemplatePage() {
  const { teamSlug, templateId } = Route.useParams();
  const { view } = Route.useSearch();
  return <ResourceTemplatePage key={`${teamSlug}:${view === true}`} teamSlug={teamSlug} templateId={templateId} viewOnly={view} />;
}
