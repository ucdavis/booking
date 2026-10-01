import {
  IdentificationIcon,
  ShieldCheckIcon,
  UserCircleIcon,
} from '@heroicons/react/24/outline';
import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { teamRoleLabels } from '@/features/teams/models/TeamRole.ts';
import { myTeamsQueryOptions } from '@/queries/teams.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';

export const Route = createFileRoute('/(authenticated)/me')({
  component: MyProfilePage,
});

function MyProfilePage() {
  const user = useUser();
  const teamsQuery = useQuery(myTeamsQueryOptions(user.id));
  const visibleTeams = teamsQuery.data?.slice(0, 5) ?? [];
  const remainingTeamCount =
    (teamsQuery.data?.length ?? 0) - visibleTeams.length;
  const name = user.name?.trim() || 'Your account';
  const nameParts = user.name?.trim().split(/\s+/).filter(Boolean) ?? [];
  const initials = [nameParts[0], nameParts.length > 1 ? nameParts.at(-1) : '']
    .map((part) => part?.[0] ?? '')
    .join('')
    .toUpperCase();

  return (
    <main className="content-container py-6 sm:py-10">
      <nav
        aria-label="Breadcrumb"
        className="mb-6 text-sm text-base-content/65"
      >
        <Link className="hover:text-primary hover:underline" to="/temp">
          Home
        </Link>{' '}
        <span aria-hidden="true">/</span>{' '}
        <span aria-current="page">My profile</span>
      </nav>

      <h1 className="text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
        My profile
      </h1>
      <p className="mt-3 text-base-content/70">
        Your account details and access in Booking.
      </p>

      {user.isEmulating && (
        <div className="alert alert-warning mt-6" role="status">
          <UserCircleIcon aria-hidden="true" className="h-6 w-6 shrink-0" />
          <div className="min-w-0">
            <p className="font-semibold">You are emulating another user</p>
            <p className="mt-1 text-sm">
              These details belong to the emulated user. Use Stop emulating in
              the account menu to return to your own account.
            </p>
          </div>
        </div>
      )}

      <div className="mt-8 grid items-start gap-6 lg:grid-cols-3">
        <section
          aria-labelledby="account-details-heading"
          className="min-w-0 overflow-hidden rounded-2xl border border-base-300 bg-base-200 shadow-sm lg:col-span-2"
        >
          <div className="h-2 bg-secondary" />
          <div className="flex flex-col gap-5 p-6 sm:flex-row sm:items-center sm:p-8">
            <div
              aria-hidden="true"
              className="flex h-20 w-20 shrink-0 items-center justify-center rounded-2xl bg-primary text-2xl font-semibold text-primary-content"
            >
              {initials || <UserCircleIcon className="h-10 w-10" />}
            </div>
            <div className="min-w-0">
              <p className="text-sm font-medium text-base-content/65">
                {user.isEmulating ? 'Emulated account' : 'Signed in as'}
              </p>
              <h2
                className="mt-1 text-2xl font-semibold break-words text-primary"
                id="account-details-heading"
              >
                {name}
              </h2>
              <p className="mt-2 text-sm text-base-content/70">
                UC Davis Booking account
              </p>
            </div>
          </div>

          <div className="border-t border-base-300 px-6 py-6 sm:px-8">
            <div className="flex items-center gap-2 text-primary">
              <IdentificationIcon aria-hidden="true" className="h-5 w-5" />
              <h3 className="font-semibold">Account details</h3>
            </div>
            <dl className="mt-5 divide-y divide-base-300">
              {[
                { label: 'Email address', value: user.email },
                { label: 'IAM ID', value: user.iamId },
                { label: 'Kerberos ID', value: user.kerberos },
              ].map(({ label, value }) => (
                <div
                  className="grid gap-1 py-4 first:pt-0 last:pb-0 sm:grid-cols-[9rem_minmax(0,1fr)] sm:gap-4"
                  key={label}
                >
                  <dt className="text-sm text-base-content/65">{label}</dt>
                  <dd className="min-w-0 text-sm font-medium break-words [overflow-wrap:anywhere]">
                    {value?.trim() || 'Not available'}
                  </dd>
                </div>
              ))}
            </dl>
          </div>
        </section>

        <section
          aria-labelledby="access-heading"
          className="min-w-0 rounded-2xl border border-base-300 bg-base-200 p-6 shadow-sm sm:p-8"
        >
          <div className="flex items-center gap-2 text-primary">
            <ShieldCheckIcon aria-hidden="true" className="h-5 w-5" />
            <h2 className="text-lg font-semibold" id="access-heading">
              Access &amp; roles
            </h2>
          </div>

          <dl className="mt-6">
            <dt className="text-sm text-base-content/65">Site access</dt>
            <dd className="mt-2">
              <span
                className={`badge badge-outline ${user.isSiteAdmin ? 'badge-primary' : ''}`}
              >
                {user.isSiteAdmin ? 'Site administrator' : 'Standard user'}
              </span>
              {user.isSiteAdmin && (
                <p className="mt-3 text-sm leading-relaxed text-base-content/70">
                  You can manage teams, resource templates, and administrator
                  access.
                </p>
              )}
              {user.isSiteAdmin && (
                <Link
                  className="btn btn-outline btn-primary btn-sm mt-4"
                  to="/admin"
                >
                  Open site administration
                </Link>
              )}
              {user.isSiteAdmin && (
                <p className="mt-3 text-sm leading-relaxed text-base-content/70">
                  As a site administrator, you also have access to all teams.
                </p>
              )}
            </dd>

            {(!teamsQuery.isSuccess || visibleTeams.length > 0) && (
              <>
                <dt className="mt-6 border-t border-base-300 pt-6 text-sm text-base-content/65">
                  Team memberships
                </dt>
                <dd className="mt-3">
                  {teamsQuery.isPending ? (
                    <p className="text-sm text-base-content/70" role="status">
                      Loading your teams…
                    </p>
                  ) : teamsQuery.isError ? (
                    <div>
                      <p className="text-sm text-error" role="alert">
                        We could not load your teams.
                      </p>
                      <button
                        className="btn btn-ghost btn-sm mt-2"
                        disabled={teamsQuery.isFetching}
                        onClick={() => void teamsQuery.refetch()}
                        type="button"
                      >
                        {teamsQuery.isFetching ? 'Retrying…' : 'Try again'}
                      </button>
                    </div>
                  ) : (
                    <>
                      <ul aria-label="Team memberships" className="space-y-2">
                        {visibleTeams.map((team) => (
                          <li
                            className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-lg bg-primary/5 px-3 py-3"
                            key={team.id}
                          >
                            <Link
                              className="min-w-0 flex-1 text-sm font-medium text-primary underline-offset-4 hover:underline [overflow-wrap:anywhere]"
                              params={{ teamSlug: team.slug }}
                              to="/teams/$teamSlug"
                            >
                              {team.name}
                            </Link>
                            <span className="badge badge-outline badge-sm shrink-0">
                              {team.role
                                ? teamRoleLabels[team.role]
                                : 'Not available'}
                            </span>
                          </li>
                        ))}
                      </ul>
                      {remainingTeamCount > 0 && (
                        <p className="mt-3 text-sm text-base-content/70">
                          And {remainingTeamCount} more{' '}
                          {remainingTeamCount === 1 ? 'team' : 'teams'}. Use the
                          team menu to see all your teams.
                        </p>
                      )}
                    </>
                  )}
                </dd>
              </>
            )}
          </dl>
        </section>
      </div>
    </main>
  );
}
