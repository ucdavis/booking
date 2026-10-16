import { AdjustmentsHorizontalIcon } from '@heroicons/react/24/outline';
import { createFileRoute, Link } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { ResourceCard } from '@/features/reservations/ResourceCard.tsx';
import {
  buildings,
  resources,
  type ResourceKind,
} from '@/features/reservations/mockData.ts';

export const Route = createFileRoute('/temp/resources/')({
  component: ResourceDirectoryPage,
  validateSearch: (search: Record<string, unknown>) => ({
    availability: search.availability === 'now' ? 'now' : undefined,
    kind: ['room', 'bench', 'equipment'].includes(String(search.kind))
      ? (search.kind as ResourceKind)
      : undefined,
    q: typeof search.q === 'string' ? search.q : undefined,
  }),
});

function ResourceDirectoryPage() {
  const initialSearch = Route.useSearch();
  const [building, setBuilding] = useState('all');
  const [kind, setKind] = useState<'all' | ResourceKind>(
    initialSearch.kind ?? 'all'
  );
  const [search, setSearch] = useState(initialSearch.q ?? '');
  const [showAvailableOnly, setShowAvailableOnly] = useState(
    initialSearch.availability === 'now'
  );

  const filteredResources = useMemo(
    () =>
      resources.filter((resource) => {
        const searchableText = [
          resource.name,
          resource.kind,
          resource.building,
          resource.parentName,
          ...resource.features,
        ]
          .filter(Boolean)
          .join(' ')
          .toLowerCase();

        return (
          (building === 'all' || resource.buildingCode === building) &&
          (kind === 'all' || resource.kind === kind) &&
          (!showAvailableOnly || resource.availability === 'available') &&
          searchableText.includes(search.trim().toLowerCase())
        );
      }),
    [building, kind, search, showAvailableOnly]
  );

  return (
    <main className="content-container py-4 sm:py-8">
      <nav aria-label="Breadcrumb" className="mb-5 text-sm text-base-content/65">
        <Link className="hover:text-primary hover:underline" to="/temp">
          Home
        </Link>{' '}
        <span aria-hidden="true">/</span>{' '}
        <span aria-current="page">Browse resources</span>
      </nav>

      <div className="max-w-3xl">
        <h1 className="mt-2 text-3xl font-semibold tracking-tight text-primary sm:text-4xl">Find available resources</h1>
        <p className="mt-3 text-lg leading-7 text-base-content/70">Search reservable rooms, lab benches, and equipment. Every result includes its building and location details.</p>
      </div>

      <section aria-labelledby="filters-heading" className="mt-8 rounded-sm border border-base-300 bg-base-200 p-5 sm:p-6">
        <div className="flex items-center gap-2 text-primary">
          <AdjustmentsHorizontalIcon aria-hidden="true" className="h-5 w-5" />
          <h2 className="font-semibold" id="filters-heading">Search and filters</h2>
        </div>
        <div className="mt-5 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <label className="sm:col-span-2">
            <span className="mb-2 block text-sm font-medium">Resource or feature</span>
            <input className="input w-full" onChange={(event) => setSearch(event.target.value)} placeholder="e.g. projector, room 1155" type="search" value={search} />
          </label>
          <label>
            <span className="mb-2 block text-sm font-medium">Building</span>
            <select className="select w-full" onChange={(event) => setBuilding(event.target.value)} value={building}>
              <option value="all">All buildings</option>
              {buildings.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
            </select>
          </label>
          <label>
            <span className="mb-2 block text-sm font-medium">Resource type</span>
            <select className="select w-full" onChange={(event) => setKind(event.target.value as 'all' | ResourceKind)} value={kind}>
              <option value="all">All resource types</option>
              <option value="room">Rooms</option>
              <option value="bench">Benches</option>
              <option value="equipment">Equipment</option>
            </select>
          </label>
        </div>
        <label className="mt-5 flex w-fit cursor-pointer items-center gap-3 text-sm">
          <input checked={showAvailableOnly} className="checkbox checkbox-primary checkbox-sm" onChange={(event) => setShowAvailableOnly(event.target.checked)} type="checkbox" />
          Show only resources available now
        </label>
      </section>

      <section aria-labelledby="results-heading" className="mt-10">
        <div className="flex flex-wrap items-baseline justify-between gap-3">
          <h2 className="text-xl font-semibold text-primary" id="results-heading">{filteredResources.length} resources found</h2>
          <p className="text-sm text-base-content/65">Availability is shown for today, September 24.</p>
        </div>
        {filteredResources.length > 0 ? (
          <div className="mt-5 grid gap-5 md:grid-cols-2 xl:grid-cols-3">
            {filteredResources.map((resource) => <ResourceCard key={resource.id} resource={resource} />)}
          </div>
        ) : (
          <div className="mt-5 rounded-sm border border-dashed border-base-300 bg-base-200 px-6 py-14 text-center">
            <h3 className="text-lg font-semibold">No resources match those filters</h3>
            <p className="mt-2 text-base-content/70">Try removing a filter or search for a different feature.</p>
          </div>
        )}
      </section>
    </main>
  );
}
