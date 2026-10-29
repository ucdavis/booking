import { createFileRoute } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { TeamSpaces } from '@/features/teams/TeamSpaces.tsx';
import { TeamPaymentsSettings } from '@/features/teams/TeamPaymentsSettings.tsx';
import { TeamRole, teamRoleLabels } from '@/features/teams/models/TeamRole.ts';
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

  const { isSiteAdmin, roles, team } = teamQuery.data;
  const accessLabels = [
    ...(isSiteAdmin ? ['Site administrator'] : []),
    ...roles.map((role) =>
      role === TeamRole.Admin ? 'Team administrator' : teamRoleLabels[role]
    ),
  ];
  const canManage = isSiteAdmin || roles.includes(TeamRole.Admin);

  return (
    <div className="grid items-start gap-6 lg:grid-cols-2">
      <section className="min-w-0 rounded-sm border border-base-300 bg-base-200 p-6 shadow-sm sm:p-8">
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
          <dd>{accessLabels.join(', ') || 'Team access'}</dd>
        </dl>
        <p className="mt-6 text-sm text-base-content/65">
          The team name and URL slug were set when this team was created and
          cannot be changed.
        </p>
        {isSiteAdmin && roles.length === 0 && (
          <p className="mt-3 text-sm text-base-content/65">
            You have access through site administration. You do not have a
            separate role on this team.
          </p>
        )}
      </section>
      <TeamSpaces
        canAttach={isSiteAdmin}
        key={`${teamSlug}:${user.id}:${isSiteAdmin}`}
        team={team}
      />
      <TeamPaymentsSettings
        canManage={canManage}
        key={`${teamSlug}:${user.id}:${canManage}`}
        teamSlug={teamSlug}
      />
    </div>
  );
}
