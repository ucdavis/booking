import { useQuery } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useState } from 'react';
import { ResourceTemplateEditor } from '@/features/resource-templates/ResourceTemplateEditor.tsx';
import { parseFormDefinition } from '@/features/resource-templates/formDefinition.ts';
import type { ResourceTemplate } from '@/features/resource-templates/models/ResourceTemplate.ts';
import { HttpError } from '@/lib/api.ts';
import { resourceTemplateQueryOptions } from '@/queries/resourceTemplates.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';

export const Route = createFileRoute('/(authenticated)/admin/resource-templates/$templateId')({
  component: EditResourceTemplatePage,
});

function EditResourceTemplatePage() {
  const { templateId } = Route.useParams();
  const navigate = Route.useNavigate();
  const id = Number(templateId);
  const validId = /^\d+$/.test(templateId) && Number.isSafeInteger(id) && id > 0 && id <= 2147483647;
  const templateQuery = useQuery({
    ...resourceTemplateQueryOptions(id),
    enabled: validId,
    refetchOnReconnect: false,
    refetchOnWindowFocus: false,
  });
  const [editSession, setEditSession] = useState<{ id: number; template?: ResourceTemplate }>({ id });
  // Preserve the opened document across refreshes; the editor owns its draft.
  // A different route ID begins a new session, including when its query is pending.
  if (!Object.is(editSession.id, id)) {
    setEditSession({ id, template: templateQuery.data });
  } else if (!editSession.template && templateQuery.data) {
    setEditSession({ id, template: templateQuery.data });
  }
  const template = Object.is(editSession.id, id) ? editSession.template ?? templateQuery.data : templateQuery.data;
  if (templateQuery.error instanceof HttpError && templateQuery.error.status === 403) return <NotAuthorizedPage />;
  if (validId && !template && templateQuery.isPending) return <main className="content-container py-8" role="status">Loading template…</main>;
  if (!validId || (!template && templateQuery.isError)) {
    const notFound = !validId || (templateQuery.error instanceof HttpError && templateQuery.error.status === 404);
    return (
      <main className="content-container py-8">
        <h1 className="text-3xl font-semibold text-primary">{notFound ? 'Template not found' : 'We could not load this template'}</h1>
        <Link className="btn btn-outline mt-6" to="/admin/resource-templates">Back to resource templates</Link>
        {!notFound && <button className="btn btn-primary ml-3 mt-6" onClick={() => void templateQuery.refetch()} type="button">Try again</button>}
      </main>
    );
  }
  if (!template) return null;
  let definition;
  try {
    definition = parseFormDefinition(template.formJson, template.formSchemaVersion);
  } catch (error) {
    return (
      <main className="content-container py-8">
        <h1 className="text-3xl font-semibold text-primary">{template.name}</h1>
        <div className="mt-6 rounded-xl border border-warning bg-warning/10 p-5" role="alert">
          <h2 className="font-semibold">This template cannot be edited with this builder</h2>
          <p className="mt-2">{error instanceof Error ? error.message : 'The saved form definition is not supported.'} The saved template has been preserved.</p>
        </div>
        <Link className="btn btn-outline mt-6" to="/admin/resource-templates">Back to resource templates</Link>
      </main>
    );
  }
  return <ResourceTemplateEditor
    definition={definition}
    key={template.id}
    onSaved={async (savedTemplate) => {
      if (savedTemplate.id !== id) {
        await navigate({
          params: { templateId: String(savedTemplate.id) },
          replace: true,
          to: '/admin/resource-templates/$templateId',
        });
      }
    }}
    template={template}
  />;
}
