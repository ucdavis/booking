import { createFileRoute } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { TeamPaymentsSettings } from '@/features/teams/TeamPaymentsSettings.tsx';
import { TeamRole } from '@/features/teams/models/TeamRole.ts';
import { teamAccessQueryOptions } from '@/queries/teams.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';

export const Route = createFileRoute('/(authenticated)/teams/$teamSlug/')({
  component: TeamOverviewPage,
});

function TeamOverviewPage() {
  const user = useUser();
  const { teamSlug } = Route.useParams();
  const teamQuery = useQuery({
    ...teamAccessQueryOptions(teamSlug),
    refetchOnMount: false,
  });

  if (!teamQuery.isSuccess) {
    return null;
  }

  const { isSiteAdmin, role, team } = teamQuery.data;
  const roleLabels = {
    admin: 'Team administrator',
    editor: 'Editor',
    viewer: 'Viewer',
  };
  const accessLabel = isSiteAdmin
    ? 'Site administrator'
    : role
      ? roleLabels[role]
      : 'Team access';

  return (
    <>
      <section className="max-w-3xl rounded-xl border border-base-300 bg-base-100 p-6 shadow-sm sm:p-8">
        <h2 className="text-xl font-semibold text-primary">Team overview</h2>
        <dl className="mt-6 grid gap-x-6 gap-y-2 sm:grid-cols-[auto_1fr]">
          <dt className="text-sm font-medium text-base-content/65">
            Team name
          </dt>
          <dd className="mb-3 break-words sm:mb-0">{team.name}</dd>
          <dt className="text-sm font-medium text-base-content/65">Team URL</dt>
          <dd className="mb-3 break-all sm:mb-0">/teams/{team.slug}</dd>
          <dt className="text-sm font-medium text-base-content/65">
            Your access
          </dt>
          <dd>{accessLabel}</dd>
        </dl>
        <p className="mt-6 text-sm text-base-content/65">
          The team name and URL slug were set when this team was created and
          cannot be changed.
        </p>
        {isSiteAdmin && !role && (
          <p className="mt-3 text-sm text-base-content/65">
            You have access through site administration. You do not have a
            separate role on this team.
          </p>
        )}
      </section>
      <TeamPaymentsSettings
        canManage={isSiteAdmin || role === TeamRole.Admin}
        key={`${teamSlug}:${user.id}:${isSiteAdmin || role === TeamRole.Admin}`}
        teamSlug={teamSlug}
      />
    </>
  );
}
