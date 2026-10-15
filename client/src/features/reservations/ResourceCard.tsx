import {
  BeakerIcon,
  BuildingOffice2Icon,
  CubeIcon,
} from '@heroicons/react/24/outline';
import { Link } from '@tanstack/react-router';
import {
  resourceKindLabels,
  type Resource,
  type ResourceKind,
} from './mockData.ts';

const kindIcons: Record<ResourceKind, typeof BuildingOffice2Icon> = {
  bench: BeakerIcon,
  equipment: CubeIcon,
  room: BuildingOffice2Icon,
};

export function ResourceCard({ resource }: { resource: Resource }) {
  const Icon = kindIcons[resource.kind];

  return (
    <article className="flex h-full flex-col rounded-sm border border-base-300 bg-base-200 p-5">
      <div className="flex items-center gap-3">
        <Icon aria-hidden="true" className="h-5 w-5 shrink-0 text-primary" />
        <div>
          <p className="text-xs font-semibold tracking-wide text-base-content/60 uppercase">
            {resourceKindLabels[resource.kind]}
          </p>
          <h3 className="font-semibold text-primary">{resource.name}</h3>
        </div>
      </div>

      <p className="mt-4 flex-1 text-sm leading-6 text-base-content/75">
        {resource.description}
      </p>

      <dl className="mt-5 space-y-2 border-t border-base-300 pt-4 text-sm">
        <div className="flex justify-between gap-4">
          <dt className="text-base-content/60">Location</dt>
          <dd className="text-right font-medium">
            {resource.building}
            {resource.parentName ? ` · ${resource.parentName}` : ''}
          </dd>
        </div>
        {resource.capacity && (
          <div className="flex justify-between gap-4">
            <dt className="text-base-content/60">Capacity</dt>
            <dd className="font-medium">
              {resource.capacity} {resource.capacity === 1 ? 'person' : 'people'}
            </dd>
          </div>
        )}
        <div className="flex justify-between gap-4">
          <dt className="text-base-content/60">Next opening</dt>
          <dd className="text-right font-medium text-success">
            {resource.nextAvailable}
          </dd>
        </div>
      </dl>

      <div className="flex items-center justify-between gap-3 pt-5">
        <div className="flex flex-wrap gap-1.5">
          {resource.reservationMode === 'child-resources' && (
            <span className="badge badge-soft badge-sm">
              Reserve by bench
            </span>
          )}
          {resource.trainingRequired && (
            <span className="badge badge-soft badge-sm">Training needed</span>
          )}
          {resource.requiresApproval && (
            <span className="badge badge-soft badge-sm">Approval needed</span>
          )}
        </div>
        <Link
          className="btn btn-primary btn-sm"
          params={{ resourceId: resource.id }}
          to="/temp/resources/$resourceId"
        >
          {resource.reservationMode === 'child-resources'
            ? 'View benches'
            : 'View'}
        </Link>
      </div>
    </article>
  );
}
