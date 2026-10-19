import { createFileRoute } from '@tanstack/react-router';
import { ResourceTemplatePage } from '@/features/resource-templates/ResourceTemplatePage.tsx';

export const Route = createFileRoute('/(authenticated)/admin/resource-templates/$templateId')({
  component: EditResourceTemplatePage,
  validateSearch: (search): { view?: boolean } => search.view === true ? { view: true } : {},
});

function EditResourceTemplatePage() {
  const { templateId } = Route.useParams();
  const { view } = Route.useSearch();
  return <ResourceTemplatePage key={String(view === true)} templateId={templateId} viewOnly={view} />;
}
