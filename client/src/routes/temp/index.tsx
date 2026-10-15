import {
  ArrowRightIcon,
  CalendarDaysIcon,
  CheckCircleIcon,
  CubeIcon,
  MagnifyingGlassIcon,
} from '@heroicons/react/24/outline';
import { createFileRoute, Link } from '@tanstack/react-router';
import {
  buildings,
  reservations,
} from '@/features/reservations/mockData.ts';

export const Route = createFileRoute('/temp/')({
  component: ReservationHomePage,
});

const dateFormatter = new Intl.DateTimeFormat('en-US', {
  day: 'numeric',
  month: 'short',
  weekday: 'short',
});

const timeFormatter = new Intl.DateTimeFormat('en-US', {
  hour: 'numeric',
  minute: '2-digit',
});

function ReservationHomePage() {
  return (
    <main className="content-container py-4 sm:py-8">
      <section className="relative isolate overflow-hidden rounded-sm border-b-8 border-secondary bg-primary px-6 py-10 text-primary-content sm:px-10 sm:py-14">
        <img
          alt=""
          aria-hidden="true"
          className="pointer-events-none absolute top-1/2 right-6 hidden h-60 w-auto -translate-y-1/2 opacity-60 lg:block"
          src="/bike-cow.svg"
        />
        <div className="relative max-w-3xl lg:w-3/5">
          <p className="mt-3 text-xl font-semibold tracking-tight sm:text-3xl">
            Reserve rooms, shared laboratory benches, and standalone equipment
            across campus.
          </p>
          <form className="mt-8 grid gap-3 rounded-lg bg-primary-content/10 p-3 sm:grid-cols-[1fr_auto]">
            <label className="relative block">
              <span className="sr-only">Search resources</span>
              <MagnifyingGlassIcon
                aria-hidden="true"
                className="absolute left-4 top-1/2 h-5 w-5 -translate-y-1/2 text-base-content/60"
              />
              <input
                className="input input-lg w-full bg-base-200 pl-12 text-base-content"
                placeholder="Search rooms, benches, equipment, or buildings"
                type="search"
              />
            </label>
            <Link className="btn btn-secondary btn-lg" to="/temp/resources">
              Search availability
            </Link>
          </form>
        </div>
      </section>

      <section aria-labelledby="browse-heading" className="mt-12">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="text-sm font-semibold tracking-wide text-base-content/60 uppercase">
              Browse by resource
            </p>
            <h2
              className="mt-1 text-2xl font-semibold text-primary"
              id="browse-heading"
            >
              What do you need to reserve?
            </h2>
          </div>
          <Link className="btn btn-ghost btn-sm" to="/temp/resources">
            See all resources <ArrowRightIcon aria-hidden="true" className="h-4 w-4" />
          </Link>
        </div>
        <div className="mt-5 grid gap-4 md:grid-cols-3">
          {[
            ['Rooms', 'Meeting, teaching, and collaboration spaces', '32'],
            ['Benches', 'Shared lab workspaces within teaching labs', '14'],
            ['Equipment', 'Standalone instruments and event equipment', '67'],
          ].map(([title, description, count]) => (
            <Link
              className="group rounded-sm border border-base-300 bg-base-200 p-5 transition hover:border-primary hover:shadow-sm"
              key={title}
              to="/temp/resources"
            >
              <div className="flex items-center justify-between">
                {title === 'Equipment' ? (
                  <CubeIcon aria-hidden="true" className="h-5 w-5 text-primary" />
                ) : (
                  <CalendarDaysIcon aria-hidden="true" className="h-5 w-5 text-primary" />
                )}
                <span className="text-sm text-base-content/60">{count} available</span>
              </div>
              <h3 className="mt-4 text-lg font-semibold text-primary">{title}</h3>
              <p className="mt-1 text-sm leading-6 text-base-content/70">
                {description}
              </p>
            </Link>
          ))}
        </div>
      </section>

      <div className="mt-14 grid gap-8 lg:grid-cols-[1.3fr_0.7fr]">
        <section
          aria-labelledby="reservations-heading"
          className="rounded-sm border border-base-300 bg-base-200 p-6 sm:p-8"
        >
          <div className="flex items-center justify-between gap-4 border-b border-base-300 pb-5">
            <div>
              <p className="text-sm font-semibold tracking-wide text-base-content/60 uppercase">
                Your schedule
              </p>
              <h2
                className="mt-1 text-xl font-semibold text-primary"
                id="reservations-heading"
              >
                Upcoming reservations
              </h2>
            </div>
            <Link className="btn btn-ghost btn-sm" to="/temp/reservations">
              View all
            </Link>
          </div>
          <ul className="divide-y divide-base-300">
            {reservations.slice(0, 2).map((reservation) => (
              <li
                className="flex flex-wrap items-center justify-between gap-4 py-5"
                key={reservation.id}
              >
                <div className="flex items-center gap-4">
                  <span className="flex h-11 w-11 flex-col items-center justify-center rounded-lg bg-primary/10 text-primary">
                    <span className="text-xs font-bold uppercase">
                      {dateFormatter.format(new Date(reservation.start)).split(' ')[0]}
                    </span>
                    <span className="text-lg font-bold leading-none">
                      {new Date(reservation.start).getDate()}
                    </span>
                  </span>
                  <div>
                    <h3 className="font-semibold">{reservation.resourceName}</h3>
                    <p className="mt-1 text-sm text-base-content/70">
                      {reservation.building} ·{' '}
                      {timeFormatter.format(new Date(reservation.start))}–
                      {timeFormatter.format(new Date(reservation.end))}
                    </p>
                  </div>
                </div>
                <span
                  className={`badge ${reservation.status === 'confirmed' ? 'badge-success' : 'badge-warning'}`}
                >
                  {reservation.status}
                </span>
              </li>
            ))}
          </ul>
        </section>

        <section
          aria-labelledby="buildings-heading"
          className="rounded-sm bg-base-300/60 p-6 sm:p-8"
        >
          <p className="text-sm font-semibold tracking-wide text-base-content/60 uppercase">
            Locations
          </p>
          <h2
            className="mt-1 text-xl font-semibold text-primary"
            id="buildings-heading"
          >
            Campus buildings
          </h2>
          <ul className="mt-5 space-y-4">
            {buildings.map((building) => (
              <li className="rounded-sm bg-base-200 p-4" key={building.code}>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h3 className="font-semibold">{building.name}</h3>
                    <p className="mt-1 text-sm text-base-content/70">
                      {building.resourceCount} reservable resources
                    </p>
                  </div>
                  <CheckCircleIcon
                    aria-hidden="true"
                    className="h-5 w-5 shrink-0 text-success"
                  />
                </div>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </main>
  );
}
