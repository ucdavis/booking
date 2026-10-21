import { createFileRoute } from '@tanstack/react-router';
import { ResourcesPage } from '@/features/resources/ResourcesPage.tsx';

export const Route = createFileRoute(
  '/(authenticated)/teams/$teamSlug/resources/'
)({
  component: TeamResourcesRoute,
  remountDeps: ({ params }) => params.teamSlug,
});

function TeamResourcesRoute() {
  const { teamSlug } = Route.useParams();
  return <ResourcesPage key={teamSlug} teamSlug={teamSlug} />;
}
