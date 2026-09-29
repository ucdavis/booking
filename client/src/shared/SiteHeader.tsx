import { Link } from '@tanstack/react-router';
import { useMeQuery } from '@/queries/user.ts';

const navigationItems = [
  ['Find resources', '/temp/resources'],
  ['My reservations', '/temp/reservations'],
  ['Manage resources', '/temp/admin/resources'],
] as const;

export function SiteHeader() {
  const userQuery = useMeQuery();

  return (
    <header className="bg-base-100">
      <div className="content-container flex flex-wrap items-center justify-between gap-4 py-4">
        <Link className="flex items-center gap-3" to="/temp">
          <img alt="Grove" className="h-10 w-auto" src="/grove-logo.svg" />
          <span>
            <span className="block text-xl font-bold leading-none tracking-tight">
              Grove
            </span>
            <span className="mt-1 block text-sm text-base-content/80">
              UC Davis reservations
            </span>
          </span>
        </Link>

        <nav
          aria-label="Primary navigation"
          className="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm font-medium"
        >
          {navigationItems.map(([label, to]) => (
            <Link
              className="text-base-content/70 transition-colors hover:text-base-content"
              key={to}
              to={to}
            >
              {label}
            </Link>
          ))}
          {userQuery.isSuccess && userQuery.data.isSiteAdmin && (
            <Link
              activeProps={{ className: 'font-semibold text-primary' }}
              className="text-base-content/70 transition-colors hover:text-base-content"
              to="/admin"
            >
              Site admin
            </Link>
          )}
        </nav>
      </div>
    </header>
  );
}
