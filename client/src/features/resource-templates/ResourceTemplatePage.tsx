import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';
import { ResourceTemplateEditor } from './ResourceTemplateEditor.tsx';
import { parseFormDefinition } from './formDefinition.ts';
import type { ResourceTemplate } from './models/ResourceTemplate.ts';
import { HttpError } from '@/lib/api.ts';
import { resourceTemplateQueryOptions } from '@/queries/resourceTemplates.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';

export function ResourceTemplatePage({
  teamSlug,
  templateId,
  viewOnly = false,
}: {
  teamSlug?: string;
  templateId: string;
  viewOnly?: boolean;
}) {
  const navigate = useNavigate();
  const id = Number(templateId);
  const validId = /^\d+$/.test(templateId) && Number.isSafeInteger(id) && id > 0 && id <= 2_147_483_647;
  const templateQuery = useQuery({
    ...resourceTemplateQueryOptions(id, teamSlug),
    enabled: validId,
    refetchOnMount: 'always',
    refetchOnReconnect: false,
    refetchOnWindowFocus: false,
  });
  const [editSession, setEditSession] = useState<{ id: number; teamSlug?: string; template?: ResourceTemplate }>({ id, teamSlug });
  const sameSession = Object.is(editSession.id, id) && editSession.teamSlug === teamSlug;
  const openedTemplate = templateQuery.isFetchedAfterMount && templateQuery.isSuccess
    ? templateQuery.data
    : undefined;
  // Revalidate a new session before taking its baseline; cached active versions
  // may have been archived since the last visit.
  // Preserve the opened document across refreshes; the editor owns its draft.
  // A different route ID or team begins a new session, including while loading.
  if (!sameSession) {
    setEditSession({ id, teamSlug, template: openedTemplate });
  } else if (!editSession.template && openedTemplate) {
    setEditSession({ id, teamSlug, template: openedTemplate });
  }
  const template = sameSession ? editSession.template ?? openedTemplate : openedTemplate;
  const Container = teamSlug ? 'section' : 'main';
  const Heading = teamSlug ? 'h2' : 'h1';
  const containerClassName = teamSlug ? undefined : 'content-container py-8';
  const listLink = teamSlug
    ? { params: { teamSlug }, to: '/teams/$teamSlug/resource-templates' as const }
    : { to: '/admin/resource-templates' as const };
  if (templateQuery.error instanceof HttpError && templateQuery.error.status === 403) {
    return teamSlug ? (
      <section>
        <h2 className="text-3xl font-semibold text-primary">Not authorized</h2>
        <p className="mt-4 text-base-content/70">You don&apos;t have permission to manage resource templates for this team.</p>
      </section>
    ) : <NotAuthorizedPage />;
  }
  if (validId && !template && !templateQuery.isError) {
    return <Container className={containerClassName} role="status">Loading template…</Container>;
  }
  const systemTemplateUnavailable = teamSlug && template?.teamId === null &&
    templateQuery.error instanceof HttpError && templateQuery.error.status === 404;
  if (!validId || systemTemplateUnavailable || (!template && templateQuery.isError)) {
    const notFound = !validId || (templateQuery.error instanceof HttpError && templateQuery.error.status === 404);
    return (
      <Container className={containerClassName}>
        <Heading className="text-3xl font-semibold text-primary">{notFound ? 'Template not found' : 'We could not load this template'}</Heading>
        <Link className="btn btn-outline mt-6" {...listLink}>Back to resource templates</Link>
        {!notFound && <button className="btn btn-primary ml-3 mt-6" onClick={() => void templateQuery.refetch()} type="button">Try again</button>}
      </Container>
    );
  }
  if (!template) {
    return null;
  }
  let definition;
  try {
    definition = parseFormDefinition(template.formJson, template.formSchemaVersion);
  } catch (error) {
    return (
      <Container className={containerClassName}>
        <Heading className="text-3xl font-semibold text-primary">{template.name}</Heading>
        <div className="mt-6 rounded-sm border border-warning bg-warning/10 p-5" role="alert">
          <h2 className="font-semibold">This template cannot be edited with this builder</h2>
          <p className="mt-2">{error instanceof Error ? error.message : 'The saved form definition is not supported.'} The saved template has been preserved.</p>
        </div>
        <Link className="btn btn-outline mt-6" {...listLink}>Back to resource templates</Link>
      </Container>
    );
  }
  return <ResourceTemplateEditor
    definition={definition}
    key={`${teamSlug ?? 'system'}:${template.id}`}
    onSaved={async (savedTemplate) => {
      if (savedTemplate.id !== id) {
        await navigate(teamSlug ? {
          params: { teamSlug, templateId: String(savedTemplate.id) },
          replace: true,
          to: '/teams/$teamSlug/resource-templates/$templateId',
        } : {
          params: { templateId: String(savedTemplate.id) },
          replace: true,
          to: '/admin/resource-templates/$templateId',
        });
      }
    }}
    teamSlug={teamSlug}
    template={template}
    viewOnly={viewOnly}
  />;
}
