import { createFileRoute } from '@tanstack/react-router';
import { ResourcePage } from '@/features/resources/ResourcePage.tsx';

export const Route = createFileRoute(
  '/(authenticated)/teams/$teamSlug/resources/$resourceId'
)({
  component: EditTeamResourceRoute,
  remountDeps: ({ params }) => `${params.teamSlug}:${params.resourceId}`,
});

function EditTeamResourceRoute() {
  const { resourceId, teamSlug } = Route.useParams();
  return (
    <ResourcePage
      key={`${teamSlug}:${resourceId}`}
      resourceId={resourceId}
      teamSlug={teamSlug}
    />
  );
}
