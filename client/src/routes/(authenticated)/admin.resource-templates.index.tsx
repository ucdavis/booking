import { DocumentDuplicateIcon, PlusIcon } from '@heroicons/react/24/outline';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createFileRoute, Link } from '@tanstack/react-router';
import type { ColumnDef } from '@tanstack/react-table';
import { useState } from 'react';
import type { ResourceTemplate } from '@/features/resource-templates/models/ResourceTemplate.ts';
import { HttpError } from '@/lib/api.ts';
import {
  duplicateResourceTemplate,
  resourceTemplateErrorMessage,
  resourceTemplateQueryOptions,
  resourceTemplatesQueryKey,
  resourceTemplatesQueryOptions,
} from '@/queries/resourceTemplates.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';
import { DataTable } from '@/shared/dataTable.tsx';

export const Route = createFileRoute('/(authenticated)/admin/resource-templates/')({
  component: ResourceTemplatesPage,
});

function ResourceTemplatesPage() {
  const templatesQuery = useQuery(resourceTemplatesQueryOptions());
  const [showArchived, setShowArchived] = useState(false);
  const queryClient = useQueryClient();
  const navigate = Route.useNavigate();
  const duplicateMutation = useMutation({
    mutationFn: duplicateResourceTemplate,
    onSuccess: async (copy) => {
      queryClient.setQueryData(resourceTemplateQueryOptions(copy.id).queryKey, copy);
      await queryClient.invalidateQueries({ queryKey: resourceTemplatesQueryKey });
      await navigate({ params: { templateId: String(copy.id) }, to: '/admin/resource-templates/$templateId' });
    },
  });
  const columns: ColumnDef<ResourceTemplate>[] = [
    {
      accessorKey: 'name',
      cell: ({ row }) => <Link className="font-semibold text-primary hover:underline" params={{ templateId: String(row.original.id) }} to="/admin/resource-templates/$templateId">{row.original.name}</Link>,
      header: 'Template name',
    },
    {
      accessorKey: 'isActive',
      cell: ({ row }) => <span className={`badge ${row.original.isActive ? 'badge-success badge-outline' : 'badge-ghost'}`}>{row.original.isActive ? 'Active' : 'Archived'}</span>,
      header: 'Status',
    },
    {
      accessorKey: 'updatedAt',
      cell: ({ row }) => new Date(row.original.updatedAt).toLocaleDateString(),
      header: 'Updated',
    },
    {
      cell: ({ row }) => <div className="flex flex-wrap gap-2">
        <Link aria-label={`Edit ${row.original.name}`} className="btn btn-ghost btn-sm" params={{ templateId: String(row.original.id) }} to="/admin/resource-templates/$templateId">Edit</Link>
        <button aria-label={`Duplicate ${row.original.name}`} className="btn btn-outline btn-sm" disabled={duplicateMutation.isPending} onClick={() => duplicateMutation.mutate(row.original.id)} type="button">
          <DocumentDuplicateIcon aria-hidden="true" className="h-4 w-4" />
          {duplicateMutation.isPending && duplicateMutation.variables === row.original.id ? 'Duplicating…' : 'Duplicate'}
        </button>
      </div>,
      header: 'Actions',
      id: 'actions',
    },
  ];
  if (templatesQuery.error instanceof HttpError && templatesQuery.error.status === 403) return <NotAuthorizedPage />;
  const templates = (templatesQuery.data ?? []).filter((template) => showArchived || template.isActive);
  const duplicateError = duplicateMutation.error instanceof HttpError
    ? duplicateMutation.error.status === 400
      ? 'This template contains an unsupported form definition and cannot be duplicated with this builder.'
      : resourceTemplateErrorMessage(duplicateMutation.error)
    : 'We could not duplicate this template. Please try again.';

  return (
    <main className="content-container py-4 sm:py-8">
      <nav aria-label="Breadcrumb" className="mb-5 text-sm text-base-content/65">
        <Link className="hover:text-primary hover:underline" to="/admin">Site admin</Link>{' / '}<span aria-current="page">Resource templates</span>
      </nav>
      <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-3xl font-semibold tracking-tight text-primary sm:text-4xl">Resource templates</h1>
          <p className="mt-3 max-w-2xl text-base-content/70">Create reusable forms for resource requests. Site templates are managed here without a team assignment.</p>
        </div>
        <Link className="btn btn-primary shrink-0" to="/admin/resource-templates/new"><PlusIcon aria-hidden="true" className="h-5 w-5" />Create template</Link>
      </div>
      <section aria-label="Resource templates" className="mt-8 rounded-xl border border-base-300 bg-base-100 p-4 shadow-sm sm:p-6">
        <label className="mb-6 flex items-center gap-3 text-sm"><input checked={showArchived} className="checkbox checkbox-sm" onChange={(event) => setShowArchived(event.target.checked)} type="checkbox" />Include archived templates</label>
        {duplicateMutation.isError && <p className="mb-4 text-error" role="alert">{duplicateError}</p>}
        {templatesQuery.isPending ? <p className="py-8 text-center text-base-content/70" role="status">Loading templates…</p>
          : templatesQuery.isError ? <div className="py-6 text-center"><p role="alert">We could not load the resource templates. Please try again.</p><button className="btn btn-outline mt-4" onClick={() => void templatesQuery.refetch()} type="button">Try again</button></div>
          : templates.length === 0 ? <p className="py-8 text-center text-base-content/70">{showArchived ? 'No templates have been created yet. Create a template to get started.' : 'No active templates. Create a template or include archived templates.'}</p>
          : <DataTable columns={columns} data={templates} filterPlaceholder="Search templates…" initialState={{ pagination: { pageSize: 10 }, sorting: [{ desc: false, id: 'name' }] }} />}
      </section>
    </main>
  );
}
