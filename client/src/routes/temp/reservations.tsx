import {
  ArrowRightIcon,
  CalendarDaysIcon,
  ClockIcon,
  MapPinIcon,
  TagIcon,
  TrashIcon,
} from '@heroicons/react/24/outline';
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

const reservationDateFormatter = new Intl.DateTimeFormat('en-US', {
  day: 'numeric',
  month: 'short',
});

function MyReservationsPage() {
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
        <span aria-current="page">My reservations</span>
      </nav>

      <div className="flex flex-wrap items-end justify-between gap-6 border-b border-base-300 pb-8">
        <div>
          <h1 className="text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
            My reservations
          </h1>
          <p className="mt-3 max-w-2xl text-base leading-7 text-base-content/70">
            Manage your upcoming room, bench, and equipment reservations.
          </p>
        </div>
        <Link className="btn btn-primary" to="/temp/resources">
          Reserve a resource
        </Link>
      </div>

      <section aria-labelledby="upcoming-heading" className="mt-8">
        <div className="flex flex-wrap items-baseline justify-between gap-3">
          <h2
            className="text-xl font-semibold text-primary"
            id="upcoming-heading"
          >
            Upcoming
          </h2>
          <p className="text-sm text-base-content/65">
            {reservations.length} scheduled
          </p>
        </div>
        <div className="mt-4 space-y-3">
          {reservations.map((reservation) => (
            <article
              className="rounded-sm border border-base-300 bg-base-200 p-5 sm:p-6"
              key={reservation.id}
            >
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="flex min-w-0 items-start gap-4">
                  <time
                    className="flex h-12 w-12 shrink-0 flex-col items-center justify-center rounded-md bg-primary/10 text-primary"
                    dateTime={reservation.start}
                  >
                    <span className="text-xs font-semibold uppercase">
                      {
                        reservationDateFormatter
                          .format(new Date(reservation.start))
                          .split(' ')[0]
                      }
                    </span>
                    <span className="text-lg font-bold leading-none">
                      {new Date(reservation.start).getDate()}
                    </span>
                  </time>
                  <div className="min-w-0">
                    <h3 className="text-lg font-semibold text-primary">
                      {reservation.resourceName}
                    </h3>
                    <p className="mt-1 text-sm text-base-content/70">
                      {reservation.purpose}
                    </p>
                  </div>
                </div>
                <span
                  className={`badge badge-lg ${reservation.status === 'confirmed' ? 'badge-success' : 'badge-warning'}`}
                >
                  {reservation.status}
                </span>
              </div>

              <dl className="mt-5 grid gap-4 border-t border-base-300 pt-5 text-sm sm:grid-cols-2 lg:grid-cols-4">
                <div className="flex items-start gap-2.5">
                  <CalendarDaysIcon
                    aria-hidden="true"
                    className="mt-0.5 h-4 w-4 shrink-0 text-base-content/55"
                  />
                  <div>
                    <dt className="text-base-content/60">Date</dt>
                    <dd className="mt-0.5 font-medium">
                      {dateFormatter.format(new Date(reservation.start))}
                    </dd>
                  </div>
                </div>
                <div className="flex items-start gap-2.5">
                  <ClockIcon
                    aria-hidden="true"
                    className="mt-0.5 h-4 w-4 shrink-0 text-base-content/55"
                  />
                  <div>
                    <dt className="text-base-content/60">Time</dt>
                    <dd className="mt-0.5 font-medium">
                      {timeFormatter.format(new Date(reservation.start))}–
                      {timeFormatter.format(new Date(reservation.end))}
                    </dd>
                  </div>
                </div>
                <div className="flex items-start gap-2.5">
                  <MapPinIcon
                    aria-hidden="true"
                    className="mt-0.5 h-4 w-4 shrink-0 text-base-content/55"
                  />
                  <div>
                    <dt className="text-base-content/60">Building</dt>
                    <dd className="mt-0.5 font-medium">
                      {reservation.building}
                    </dd>
                  </div>
                </div>
                <div className="flex items-start gap-2.5">
                  <TagIcon
                    aria-hidden="true"
                    className="mt-0.5 h-4 w-4 shrink-0 text-base-content/55"
                  />
                  <div>
                    <dt className="text-base-content/60">Resource type</dt>
                    <dd className="mt-0.5 font-medium capitalize">
                      {reservation.resourceKind}
                    </dd>
                  </div>
                </div>
              </dl>

              <div className="mt-5 flex flex-wrap justify-end gap-2 border-t border-base-300 pt-4">
                <button
                  aria-label="Cancel reservation"
                  className="btn btn-sm"
                  title="Cancel reservation"
                  type="button"
                >
                  <TrashIcon aria-hidden="true" className="h-4 w-4" />
                  Cancel reservation
                </button>
                <Link
                  className="btn btn-sm"
                  params={{ resourceId: reservation.resourceId }}
                  to="/temp/resources/$resourceId"
                >
                  View resource
                  <ArrowRightIcon aria-hidden="true" className="h-4 w-4" />
                </Link>
              </div>
            </article>
          ))}
        </div>
      </section>

      <section
        aria-labelledby="past-heading"
        className="mt-10 flex flex-wrap items-center gap-4 rounded-sm border border-dashed border-base-300 bg-base-200 px-6 py-8"
      >
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-md bg-base-300/70 text-base-content/60">
          <ClockIcon aria-hidden="true" className="h-5 w-5" />
        </span>
        <div>
          <h2 className="text-lg font-semibold text-primary" id="past-heading">
            Past reservations
          </h2>
          <p className="mt-1 text-base-content/70">
            Your completed reservations will appear here.
          </p>
        </div>
      </section>
    </main>
  );
}
