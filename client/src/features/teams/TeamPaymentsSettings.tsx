import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useState } from 'react';
import { EditTeamPaymentsDialog } from './EditTeamPaymentsDialog.tsx';
import { HttpError } from '@/lib/api.ts';
import {
  teamAccessQueryOptions,
  teamPaymentsQueryOptions,
} from '@/queries/teams.ts';
import { useUser } from '@/shared/auth/UserContext.tsx';

export function TeamPaymentsSettings({
  canManage,
  teamSlug,
}: {
  canManage: boolean;
  teamSlug: string;
}) {
  const user = useUser();
  const queryClient = useQueryClient();
  const [isEditing, setIsEditing] = useState(false);
  const [accessDenied, setAccessDenied] = useState(false);
  const [notice, setNotice] = useState('');
  const paymentsQuery = useQuery({
    ...teamPaymentsQueryOptions(teamSlug, user.id),
    enabled: !accessDenied,
  });
  const queryAccessDenied =
    paymentsQuery.error instanceof HttpError &&
    paymentsQuery.error.status === 403;
  const isAccessDenied = accessDenied || queryAccessDenied;
  const denyAccess = useCallback(() => {
    setAccessDenied(true);
    setIsEditing(false);
    void queryClient.invalidateQueries({
      queryKey: teamAccessQueryOptions(teamSlug).queryKey,
    });
  }, [queryClient, teamSlug]);

  useEffect(() => {
    if (queryAccessDenied) {
      void queryClient.invalidateQueries({
        queryKey: teamAccessQueryOptions(teamSlug).queryKey,
      });
    }
  }, [queryAccessDenied, queryClient, teamSlug]);

  const settings = paymentsQuery.isError ? undefined : paymentsQuery.data;
  const statusLabels = {
    invalid: 'API key is invalid or disabled',
    unavailable: 'Unable to verify the connection',
    unconfigured: 'Not configured',
    valid: 'Connected',
  };
  const statusMessages = {
    invalid:
      'Payments no longer accepts the saved API key. A team administrator must replace it.',
    unavailable:
      'The connection could not be checked right now. Try checking it again.',
    unconfigured:
      'A team administrator can add a Payments API key to connect this team.',
    valid: 'The saved API key was accepted by Payments.',
  };

  return (
    <section
      aria-labelledby="team-payments-title"
      className="mt-6 max-w-3xl rounded-xl border border-base-300 bg-base-100 p-6 shadow-sm sm:p-8"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2
          className="text-xl font-semibold text-primary"
          id="team-payments-title"
        >
          Payments
        </h2>
        {canManage && !isAccessDenied && (
          <button
            className="btn btn-outline btn-sm"
            onClick={() => setIsEditing(true)}
            type="button"
          >
            {settings?.maskedApiKey || settings?.paymentsTeamSlug
              ? 'Replace API key'
              : 'Set API key'}
          </button>
        )}
      </div>
      {isAccessDenied ? (
        <p className="alert alert-warning mt-5" role="alert">
          Your access to these payments settings has changed. Reopen the team to
          check your permissions.
        </p>
      ) : paymentsQuery.isPending ? (
        <p className="mt-5 text-base-content/70" role="status">
          Loading payments settings…
        </p>
      ) : (
        <>
          {settings ? (
            <>
              <p
                className={`mt-5 font-semibold ${settings.status === 'invalid' ? 'text-error' : settings.status === 'unavailable' ? 'text-warning' : 'text-base-content'}`}
                role={
                  settings.status === 'invalid' ||
                  settings.status === 'unavailable'
                    ? 'alert'
                    : 'status'
                }
              >
                {statusLabels[settings.status]}
              </p>
              <p className="mt-2 text-sm text-base-content/70">
                {settings.message || statusMessages[settings.status]}
              </p>
              <dl className="mt-5 grid gap-x-6 gap-y-2 sm:grid-cols-[auto_1fr]">
                {settings.paymentsTeamName && (
                  <>
                    <dt className="text-sm font-medium text-base-content/65">
                      Payments team
                    </dt>
                    <dd className="break-words">{settings.paymentsTeamName}</dd>
                  </>
                )}
                {settings.paymentsTeamSlug && (
                  <>
                    <dt className="text-sm font-medium text-base-content/65">
                      Payments team slug
                    </dt>
                    <dd className="break-all">{settings.paymentsTeamSlug}</dd>
                  </>
                )}
                {canManage && settings.maskedApiKey && (
                  <>
                    <dt className="text-sm font-medium text-base-content/65">
                      Saved API key
                    </dt>
                    <dd className="break-all font-mono">
                      {settings.maskedApiKey}
                    </dd>
                  </>
                )}
              </dl>
            </>
          ) : (
            <p className="alert alert-warning mt-5" role="alert">
              We could not load the payments settings. Please try again.
            </p>
          )}
          <button
            className="btn btn-ghost btn-sm mt-4"
            disabled={paymentsQuery.isFetching}
            onClick={() => {
              setNotice('');
              void paymentsQuery.refetch();
            }}
            type="button"
          >
            {paymentsQuery.isFetching
              ? 'Checking connection…'
              : 'Check connection'}
          </button>
        </>
      )}
      {notice && (
        <p className="mt-4 text-sm text-success" role="status">
          {notice}
        </p>
      )}
      {isEditing && canManage && !isAccessDenied && (
        <EditTeamPaymentsDialog
          isReplacing={!!(settings?.maskedApiKey || settings?.paymentsTeamSlug)}
          onAccessDenied={denyAccess}
          onClose={() => setIsEditing(false)}
          onSaved={async (savedSettings) => {
            await queryClient.cancelQueries({
              queryKey: teamPaymentsQueryOptions(teamSlug, user.id).queryKey,
            });
            queryClient.setQueryData(
              teamPaymentsQueryOptions(teamSlug, user.id).queryKey,
              savedSettings
            );
            setIsEditing(false);
            setNotice('Payments API key verified and saved.');
          }}
          teamSlug={teamSlug}
        />
      )}
    </section>
  );
}
