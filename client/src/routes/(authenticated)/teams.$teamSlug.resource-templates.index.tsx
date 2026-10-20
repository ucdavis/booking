import { createFileRoute } from '@tanstack/react-router';
import { ResourceTemplatesPage } from '@/features/resource-templates/ResourceTemplatesPage.tsx';

export const Route = createFileRoute('/(authenticated)/teams/$teamSlug/resource-templates/')({
  component: TeamResourceTemplatesPage,
});

function TeamResourceTemplatesPage() {
  const { teamSlug } = Route.useParams();
  return <ResourceTemplatesPage key={teamSlug} teamSlug={teamSlug} />;
}
