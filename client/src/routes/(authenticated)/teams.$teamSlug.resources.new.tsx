import { createFileRoute } from '@tanstack/react-router';
import { ResourcePage } from '@/features/resources/ResourcePage.tsx';

export const Route = createFileRoute(
  '/(authenticated)/teams/$teamSlug/resources/new'
)({
  component: NewTeamResourceRoute,
  remountDeps: ({ params }) => params.teamSlug,
});

function NewTeamResourceRoute() {
  const { teamSlug } = Route.useParams();
  return <ResourcePage key={`${teamSlug}:new`} teamSlug={teamSlug} />;
}
