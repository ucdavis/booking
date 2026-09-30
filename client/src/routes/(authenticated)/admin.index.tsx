import {
  ArrowRightIcon,
  ShieldCheckIcon,
  UserGroupIcon,
  UsersIcon,
} from '@heroicons/react/24/outline';
import { createFileRoute, Link } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/admin/')({
  component: SiteAdminPage,
});

function SiteAdminPage() {
  return (
    <main className="content-container py-4 sm:py-8">
      <nav
        aria-label="Breadcrumb"
        className="mb-5 text-sm text-base-content/65"
      >
        <Link className="hover:text-primary hover:underline" to="/temp">
          Home
        </Link>{' '}
        <span aria-hidden="true">/</span>{' '}
        <span aria-current="page">Site admin</span>
      </nav>

      <div className="flex items-center gap-3 text-primary">
        <ShieldCheckIcon aria-hidden="true" className="h-6 w-6" />
        <p className="text-sm font-semibold tracking-wide uppercase">
          Booking administration
        </p>
      </div>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
        Site administration
      </h1>
      <p className="mt-3 max-w-2xl text-lg text-base-content/70">
        Manage access and administration across Booking.
      </p>

      <section
        aria-label="Administration tools"
        className="mt-10 grid gap-6 md:grid-cols-2 xl:grid-cols-3"
      >
        <Link
          className="group flex flex-col rounded-2xl border border-base-300 bg-base-100 p-6 shadow-sm transition hover:border-primary/40 hover:shadow-md focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary sm:p-8"
          to="/admin/users"
        >
          <span className="flex h-14 w-14 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <UsersIcon aria-hidden="true" className="h-7 w-7" />
          </span>
          <h2 className="mt-6 text-xl font-semibold text-primary">
            Site admin users
          </h2>
          <p className="mt-3 grow text-base-content/70">
            View site administrators and manage who has access to site
            administration.
          </p>
          <span className="mt-6 flex items-center gap-2 font-semibold text-primary">
            Manage admin users
            <ArrowRightIcon
              aria-hidden="true"
              className="h-4 w-4 transition-transform group-hover:translate-x-1"
            />
          </span>
        </Link>
        <Link
          className="group flex flex-col rounded-2xl border border-base-300 bg-base-100 p-6 shadow-sm transition hover:border-primary/40 hover:shadow-md focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-primary sm:p-8"
          to="/admin/teams"
        >
          <span className="flex h-14 w-14 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <UserGroupIcon aria-hidden="true" className="h-7 w-7" />
          </span>
          <h2 className="mt-6 text-xl font-semibold text-primary">Teams</h2>
          <p className="mt-3 grow text-base-content/70">
            View all teams, open team administration, and create new teams.
          </p>
          <span className="mt-6 flex items-center gap-2 font-semibold text-primary">
            Manage teams
            <ArrowRightIcon
              aria-hidden="true"
              className="h-4 w-4 transition-transform group-hover:translate-x-1"
            />
          </span>
        </Link>
      </section>
      <p className="mt-8 max-w-2xl text-sm text-base-content/60">
        Site administrators have access across Booking. Team administrators manage
        their own teams and resources; team permissions do not grant access
        here.
      </p>
    </main>
  );
}
