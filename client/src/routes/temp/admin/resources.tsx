import { PlusIcon, WrenchScrewdriverIcon } from '@heroicons/react/24/outline';
import { createFileRoute, Link } from '@tanstack/react-router';
import { resourceKindLabels, resources } from '@/features/reservations/mockData.ts';

export const Route = createFileRoute('/temp/admin/resources')({
  component: ManageResourcesPage,
});

function ManageResourcesPage() {
  return (
    <main className="content-container py-4 sm:py-8">
      <nav aria-label="Breadcrumb" className="mb-5 text-sm text-base-content/65">
        <Link className="hover:text-primary hover:underline" to="/temp">Home</Link>{' '}
        <span aria-hidden="true">/</span>{' '}
        <span aria-current="page">Manage resources</span>
      </nav>
      <div className="flex flex-wrap items-end justify-between gap-5">
        <div>
          <h1 className="text-3xl font-semibold tracking-tight text-primary sm:text-4xl">Resource inventory</h1>
          <p className="mt-3 max-w-2xl text-lg text-base-content/70">Maintain buildings, rooms, benches, and standalone equipment from one inventory.</p>
        </div>
        <button className="btn btn-primary" type="button"><PlusIcon aria-hidden="true" className="h-5 w-5" /> Add resource</button>
      </div>

      <section aria-labelledby="inventory-heading" className="mt-10 overflow-hidden rounded-sm border border-base-300 bg-base-200">
        <div className="flex flex-wrap items-center justify-between gap-4 border-b border-base-300 px-5 py-5 sm:px-6">
          <div><h2 className="font-semibold text-primary" id="inventory-heading">All resources</h2><p className="mt-1 text-sm text-base-content/65">{resources.length} items shown</p></div>
          <label className="input input-sm flex items-center gap-2"><span className="sr-only">Search inventory</span><input placeholder="Search inventory" type="search" /></label>
        </div>
        <div className="overflow-x-auto">
          <table className="table table-zebra min-w-3xl">
            <caption className="sr-only">Reservable resource inventory</caption>
            <thead><tr><th>Resource</th><th>Type</th><th>Reservation unit</th><th>Building</th><th>Location</th><th>Requirements</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>
              {resources.map((resource) => (
                <tr key={resource.id}>
                  <td><div className="font-semibold">{resource.name}</div><div className="mt-1 text-xs text-base-content/60">{resource.id}</div></td>
                  <td><span className="badge badge-outline badge-sm">{resourceKindLabels[resource.kind]}</span></td>
                  <td className="text-sm">{resource.reservationMode === 'child-resources' ? 'Child benches only' : 'Entire resource'}</td>
                  <td>{resource.building}</td>
                  <td>{resource.parentName ?? resource.floor ?? '—'}</td>
                  <td className="text-sm">{resource.trainingRequired ? 'Training' : resource.requiresApproval ? 'Approval' : 'None'}</td>
                  <td><span className={`badge badge-sm ${resource.availability === 'available' ? 'badge-success' : resource.availability === 'limited' ? 'badge-warning' : 'badge-error'}`}>{resource.availability}</span></td>
                  <td><Link className="btn btn-ghost btn-sm" params={{ resourceId: resource.id }} to="/temp/resources/$resourceId">Edit</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
      <aside className="alert mt-8 bg-primary/10 text-base-content"><WrenchScrewdriverIcon aria-hidden="true" className="h-5 w-5 text-primary" /><div><h2 className="font-semibold">Front-end prototype</h2><p className="text-sm">Inventory actions are intentionally visual only until the API and permissions are in place.</p></div></aside>
    </main>
  );
}
