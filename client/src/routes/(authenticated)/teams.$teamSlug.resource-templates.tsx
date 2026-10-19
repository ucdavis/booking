import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Outlet } from '@tanstack/react-router';
import { TeamRole } from '@/features/teams/models/TeamRole.ts';
import { teamAccessQueryOptions } from '@/queries/teams.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';

export const Route = createFileRoute('/(authenticated)/teams/$teamSlug/resource-templates')({
  component: TeamResourceTemplatesLayout,
  remountDeps: ({ params }) => params.teamSlug,
});

function TeamResourceTemplatesLayout() {
  const { teamSlug } = Route.useParams();
  const accessQuery = useQuery({ ...teamAccessQueryOptions(teamSlug), refetchOnMount: false });
  if (!accessQuery.isSuccess) {
    return null;
  }
  const { isSiteAdmin, role } = accessQuery.data;
  if (!isSiteAdmin && role !== TeamRole.Admin && role !== TeamRole.Editor) {
    return <NotAuthorizedPage description="You need Editor or administrator access to manage this team's resource templates." />;
  }
  return <Outlet />;
}
