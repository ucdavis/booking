import { DocumentDuplicateIcon, PlusIcon } from '@heroicons/react/24/outline';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import type { ColumnDef } from '@tanstack/react-table';
import { type FocusEvent, type MouseEvent, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { ResourceTemplate } from './models/ResourceTemplate.ts';
import { HttpError } from '@/lib/api.ts';
import {
  duplicateResourceTemplate,
  resourceTemplateErrorMessage,
  resourceTemplateQueryOptions,
  resourceTemplatesKey,
  resourceTemplatesQueryOptions,
} from '@/queries/resourceTemplates.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';
import { DataTable } from '@/shared/dataTable.tsx';

const resourceTemplateLink = (id: number, teamSlug?: string, view = false) => teamSlug
  ? { params: { teamSlug, templateId: String(id) }, search: view ? { view: true } : {}, to: '/teams/$teamSlug/resource-templates/$templateId' } as const
  : { params: { templateId: String(id) }, search: view ? { view: true } : {}, to: '/admin/resource-templates/$templateId' } as const;

function ResourceTemplateName({ teamSlug, template }: { teamSlug?: string; template: ResourceTemplate }) {
  const descriptionId = useId();
  const tooltipRef = useRef<HTMLDivElement>(null);
  const description = template.description?.trim();
  const [tooltip, setTooltip] = useState<{
    anchor: HTMLAnchorElement;
    bottom?: number;
    left: number;
    maxHeight: number;
    top?: number;
  } | null>(null);
  const showDescription = (anchor: HTMLAnchorElement) => {
    if (!description) {
      return;
    }
    const bounds = anchor.getBoundingClientRect();
    const below = window.innerHeight - bounds.bottom;
    const above = below < 240 && bounds.top > below;
    setTooltip({
      anchor,
      left: Math.max(8, Math.min(bounds.left, window.innerWidth - 392)),
      maxHeight: Math.min(240, Math.max(0, (above ? bounds.top : below) - 8)),
      ...(above ? { bottom: window.innerHeight - bounds.top } : { top: bounds.bottom }),
    });
  };
  const hideDescription = (nextTarget: EventTarget | null) => {
    const nextNode = nextTarget && 'nodeType' in nextTarget ? nextTarget as Node : null;
    if (nextNode && (tooltipRef.current?.contains(nextNode) || tooltip?.anchor.contains(nextNode))) {
      return;
    }
    if (tooltip?.anchor === document.activeElement || tooltipRef.current?.matches(':hover')) {
      return;
    }
    setTooltip(null);
  };
  useEffect(() => {
    if (!tooltip) {
      return;
    }
    const dismiss = () => setTooltip(null);
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        dismiss();
      }
    };
    const onScroll = (event: Event) => {
      if (!(event.target && 'nodeType' in event.target && tooltipRef.current?.contains(event.target as Node))) {
        dismiss();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('resize', dismiss);
    window.addEventListener('scroll', onScroll, true);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('resize', dismiss);
      window.removeEventListener('scroll', onScroll, true);
    };
  }, [tooltip]);

  return <>
    <Link
      aria-describedby={description ? descriptionId : undefined}
      className="font-semibold text-primary hover:underline"
      onBlur={(event: FocusEvent<HTMLAnchorElement>) => {
        if (!event.currentTarget.matches(':hover')) {
          hideDescription(event.relatedTarget);
        }
      }}
      onFocus={(event: FocusEvent<HTMLAnchorElement>) => showDescription(event.currentTarget)}
      onMouseEnter={(event: MouseEvent<HTMLAnchorElement>) => showDescription(event.currentTarget)}
      onMouseLeave={(event: MouseEvent<HTMLAnchorElement>) => hideDescription(event.relatedTarget)}
      {...resourceTemplateLink(template.id, teamSlug)}
    >{template.name}</Link>
    {description && !tooltip && <span className="sr-only" id={descriptionId}>{description}</span>}
    {tooltip && createPortal(
      <div
        className="fixed z-50 w-96 max-w-[calc(100vw-1rem)] overflow-y-auto rounded-sm border border-base-300 bg-base-100 p-3 text-sm whitespace-pre-wrap shadow-lg [overflow-wrap:anywhere]"
        id={descriptionId}
        onMouseLeave={(event) => hideDescription(event.relatedTarget)}
        ref={tooltipRef}
        role="tooltip"
        style={{ bottom: tooltip.bottom, left: tooltip.left, maxHeight: tooltip.maxHeight, top: tooltip.top }}
      >{description}</div>,
      document.body
    )}
  </>;
}

export function ResourceTemplatesPage({ teamSlug }: { teamSlug?: string }) {
  const templatesQuery = useQuery(resourceTemplatesQueryOptions(teamSlug));
  const [showArchived, setShowArchived] = useState(false);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const templateLink = (id: number, view = false) => resourceTemplateLink(id, teamSlug, view);
  const createLink = teamSlug
    ? { params: { teamSlug }, to: '/teams/$teamSlug/resource-templates/new' } as const
    : { to: '/admin/resource-templates/new' } as const;
  const duplicateMutation = useMutation({
    mutationFn: (id: number) => duplicateResourceTemplate(id, teamSlug),
    onSuccess: async (copy) => {
      queryClient.setQueryData(resourceTemplateQueryOptions(copy.id, teamSlug).queryKey, copy);
      await queryClient.invalidateQueries({ queryKey: resourceTemplatesKey(teamSlug) });
      await navigate(templateLink(copy.id));
    },
  });
  const isSystemTemplate = (template: ResourceTemplate) => !!teamSlug && template.teamId === null;
  const columns: ColumnDef<ResourceTemplate>[] = [
    {
      accessorKey: 'name',
      cell: ({ row }) => <ResourceTemplateName teamSlug={teamSlug} template={row.original} />,
      header: 'Template name',
    },
    {
      accessorKey: 'formSchemaVersion',
      header: 'Version',
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
      cell: ({ row }) => {
        const template = row.original;
        const canEdit = template.isActive && !isSystemTemplate(template);
        const duplicateLabel = isSystemTemplate(template) ? 'Duplicate into team' : 'Duplicate';
        return <div className="flex flex-wrap gap-2">
          <Link aria-label={`View ${template.name}`} className="btn btn-ghost btn-sm w-16" {...templateLink(template.id, true)}>View</Link>
          {canEdit && <Link aria-label={`Edit ${template.name}`} className="btn btn-ghost btn-sm w-16" {...templateLink(template.id)}>Edit</Link>}
          <button aria-label={`Duplicate ${template.name}${isSystemTemplate(template) ? ' into team' : ''}`} className="btn btn-outline btn-sm" disabled={duplicateMutation.isPending} onClick={() => duplicateMutation.mutate(template.id)} type="button">
            <DocumentDuplicateIcon aria-hidden="true" className="h-4 w-4" />
            {duplicateMutation.isPending && duplicateMutation.variables === template.id ? 'Duplicating…' : duplicateLabel}
          </button>
        </div>;
      },
      header: 'Actions',
      id: 'actions',
    },
  ];
  if (templatesQuery.error instanceof HttpError && templatesQuery.error.status === 403) {
    return <NotAuthorizedPage />;
  }
  const allTemplates = templatesQuery.data ?? [];
  const sections = teamSlug ? [
    { isSystem: false, templates: allTemplates.filter((template) => template.teamId !== null && (showArchived || template.isActive)), title: 'Team templates' },
    { isSystem: true, templates: allTemplates.filter((template) => template.teamId === null && template.isActive), title: 'System templates' },
  ] : [
    { isSystem: false, templates: allTemplates.filter((template) => showArchived || template.isActive), title: 'Resource templates' },
  ];
  const duplicateError = duplicateMutation.error instanceof HttpError
    ? duplicateMutation.error.status === 400
      ? 'This template contains an unsupported form definition and cannot be duplicated with this builder.'
      : resourceTemplateErrorMessage(duplicateMutation.error)
    : 'We could not duplicate this template. Please try again.';
  const Container = teamSlug ? 'section' : 'main';
  const Heading = teamSlug ? 'h2' : 'h1';

  return (
    <Container className={teamSlug ? undefined : 'content-container py-4 sm:py-8'}>
      {!teamSlug && <nav aria-label="Breadcrumb" className="mb-5 text-sm text-base-content/65">
        <Link className="hover:text-primary hover:underline" to="/admin">Site admin</Link>{' / '}<span aria-current="page">Resource templates</span>
      </nav>}
      <div className="flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <Heading className="text-2xl font-semibold tracking-tight text-primary">Resource templates</Heading>
          <p className="mt-3 max-w-2xl text-base-content/70">{teamSlug
            ? 'Manage reusable forms for your team. Start with a system template or create your own, and view older team versions by including archived templates.'
            : 'Create reusable forms for resource requests. Site templates are managed here without a team assignment.'}</p>
        </div>
        <Link className="btn btn-primary shrink-0 self-start" {...createLink}><PlusIcon aria-hidden="true" className="h-5 w-5" />Create template</Link>
      </div>
      {duplicateMutation.isError && <p className="mt-6 text-error" role="alert">{duplicateError}</p>}
      {templatesQuery.isPending ? <p className="py-8 text-center text-base-content/70" role="status">Loading templates…</p>
        : templatesQuery.isError ? <div className="py-6 text-center"><p role="alert">We could not load the resource templates. Please try again.</p><button className="btn btn-outline mt-4" onClick={() => void templatesQuery.refetch()} type="button">Try again</button></div>
        : sections.map(({ isSystem, templates, title }) => (
          <section aria-label={title} className="mt-8 rounded-sm border border-base-300 bg-base-200 p-4 shadow-sm sm:p-6" key={title}>
            {teamSlug && <h3 className="mb-4 text-xl font-semibold text-primary">{title}</h3>}
            {isSystem
              ? <p className="mb-6 text-sm text-base-content/70">View a system template or duplicate it into your team to customize it.</p>
              : <label className="mb-6 flex items-center gap-3 text-sm"><input checked={showArchived} className="checkbox checkbox-sm" onChange={(event) => setShowArchived(event.target.checked)} type="checkbox" />Include archived templates</label>}
            {templates.length === 0 ? <p className="py-8 text-center text-base-content/70">{isSystem
              ? 'No active system templates are available.'
              : showArchived ? 'No templates have been created yet. Create a template to get started.' : 'No active templates. Create a template or include archived templates.'}</p>
              : <DataTable columns={columns} data={templates} filterPlaceholder="Search templates…" initialState={{ pagination: { pageSize: 10 }, sorting: [{ desc: false, id: 'name' }] }} />}
          </section>
        ))}
    </Container>
  );
}
