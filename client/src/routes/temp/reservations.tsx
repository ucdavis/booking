import { CalendarDaysIcon, ClockIcon, MapPinIcon } from '@heroicons/react/24/outline';
import { createFileRoute, Link } from '@tanstack/react-router';
import { reservations } from '@/features/reservations/mockData.ts';

export const Route = createFileRoute('/temp/reservations')({
  component: MyReservationsPage,
});

const dateFormatter = new Intl.DateTimeFormat('en-US', {
  day: 'numeric',
  month: 'long',
  weekday: 'long',
  year: 'numeric',
});

const timeFormatter = new Intl.DateTimeFormat('en-US', {
  hour: 'numeric',
  minute: '2-digit',
});

function MyReservationsPage() {
  return (
    <main className="content-container py-4 sm:py-8">
      <nav aria-label="Breadcrumb" className="mb-5 text-sm text-base-content/65">
        <Link className="hover:text-primary hover:underline" to="/temp">Home</Link>{' '}
        <span aria-hidden="true">/</span>{' '}
        <span aria-current="page">My reservations</span>
      </nav>
      <div className="flex flex-wrap items-end justify-between gap-5">
        <div>
          <p className="text-sm font-semibold tracking-wide text-primary uppercase">Your schedule</p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight text-primary sm:text-4xl">My reservations</h1>
          <p className="mt-3 max-w-2xl text-lg text-base-content/70">Manage your upcoming room, bench, and equipment reservations.</p>
        </div>
        <Link className="btn btn-primary" to="/temp/resources">Reserve a resource</Link>
      </div>

      <section aria-labelledby="upcoming-heading" className="mt-10">
        <h2 className="text-xl font-semibold text-primary" id="upcoming-heading">Upcoming</h2>
        <div className="mt-5 space-y-4">
          {reservations.map((reservation) => (
            <article className="rounded-sm border border-base-300 bg-base-200 p-5 sm:p-6" key={reservation.id}>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="flex min-w-0 items-start gap-4">
                  <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary"><CalendarDaysIcon aria-hidden="true" className="h-6 w-6" /></span>
                  <div>
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-2"><h3 className="text-lg font-semibold text-primary">{reservation.resourceName}</h3><span className="badge badge-outline badge-sm capitalize">{reservation.resourceKind}</span></div>
                    <p className="mt-1 text-sm text-base-content/70">{reservation.purpose}</p>
                  </div>
                </div>
                <span className={`badge badge-lg ${reservation.status === 'confirmed' ? 'badge-success' : 'badge-warning'}`}>{reservation.status}</span>
              </div>
              <dl className="mt-5 grid gap-3 border-t border-base-300 pt-5 text-sm sm:grid-cols-2 lg:grid-cols-3">
                <div className="flex items-start gap-2"><CalendarDaysIcon aria-hidden="true" className="mt-0.5 h-4 w-4 shrink-0 text-base-content/55" /><div><dt className="text-base-content/60">Date</dt><dd className="mt-0.5 font-medium">{dateFormatter.format(new Date(reservation.start))}</dd></div></div>
                <div className="flex items-start gap-2"><ClockIcon aria-hidden="true" className="mt-0.5 h-4 w-4 shrink-0 text-base-content/55" /><div><dt className="text-base-content/60">Time</dt><dd className="mt-0.5 font-medium">{timeFormatter.format(new Date(reservation.start))}–{timeFormatter.format(new Date(reservation.end))}</dd></div></div>
                <div className="flex items-start gap-2"><MapPinIcon aria-hidden="true" className="mt-0.5 h-4 w-4 shrink-0 text-base-content/55" /><div><dt className="text-base-content/60">Building</dt><dd className="mt-0.5 font-medium">{reservation.building}</dd></div></div>
              </dl>
              <div className="mt-5 flex flex-wrap justify-end gap-3"><Link className="btn btn-outline btn-sm" params={{ resourceId: reservation.resourceId }} to="/temp/resources/$resourceId">View resource</Link><button className="btn btn-ghost btn-sm" type="button">Cancel reservation</button></div>
            </article>
          ))}
        </div>
      </section>

      <section aria-labelledby="past-heading" className="mt-12 rounded-sm border border-dashed border-base-300 bg-base-200 px-6 py-10">
        <h2 className="text-lg font-semibold text-primary" id="past-heading">Past reservations</h2>
        <p className="mt-2 text-base-content/70">Your completed reservations will appear here.</p>
      </section>
    </main>
  );
}
