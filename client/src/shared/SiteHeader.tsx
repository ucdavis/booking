import { ChevronDownIcon } from '@heroicons/react/24/outline';
import { Link, useLocation } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import { TeamAdminMenu } from '@/features/teams/TeamAdminMenu.tsx';
import { useMeQuery } from '@/queries/user.ts';

const navigationItems = [
  ['Find resources', '/temp/resources'],
  ['My reservations', '/temp/reservations'],
  ['Manage resources', '/temp/admin/resources'],
] as const;

export function SiteHeader() {
  const userQuery = useMeQuery();
  const pathname = useLocation({ select: (location) => location.pathname });
  const [adminMenuOpen, setAdminMenuOpen] = useState(false);
  const adminMenuRef = useRef<HTMLDivElement>(null);
  const adminButtonRef = useRef<HTMLButtonElement>(null);
  const isAdminPage = pathname === '/admin' || pathname.startsWith('/admin/');

  useEffect(() => {
    if (!adminMenuOpen) {
      return;
    }

    const closeOnOutsideClick = (event: PointerEvent) => {
      if (!adminMenuRef.current?.contains(event.target as Node | null)) {
        setAdminMenuOpen(false);
      }
    };
    document.addEventListener('pointerdown', closeOnOutsideClick);
    return () =>
      document.removeEventListener('pointerdown', closeOnOutsideClick);
  }, [adminMenuOpen]);

  return (
    <header className="bg-base-100">
      <div className="content-container flex flex-wrap items-center justify-between gap-4 py-4">
        <Link className="flex items-center gap-3" to="/temp">
          <img alt="Booking" className="h-10 w-auto" src="/booking-logo.svg" />
          <span>
            <span className="block text-xl font-bold leading-none tracking-tight">
              Booking
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
          {userQuery.isSuccess && (
            <TeamAdminMenu
              isSiteAdmin={userQuery.data.isSiteAdmin}
              key={userQuery.data.id}
              userId={userQuery.data.id}
            />
          )}
          {userQuery.isSuccess && userQuery.data.isSiteAdmin && (
            <div
              className="relative"
              onBlur={(event) => {
                if (!event.currentTarget.contains(event.relatedTarget)) {
                  setAdminMenuOpen(false);
                }
              }}
              onKeyDown={(event) => {
                if (event.key === 'Escape') {
                  setAdminMenuOpen(false);
                  adminButtonRef.current?.focus();
                }
              }}
              ref={adminMenuRef}
            >
              <button
                aria-controls="site-admin-navigation"
                aria-expanded={adminMenuOpen}
                className={`flex items-center gap-1.5 transition-colors hover:text-primary ${isAdminPage ? 'font-semibold text-primary' : 'text-base-content/70'}`}
                onClick={() => setAdminMenuOpen((open) => !open)}
                ref={adminButtonRef}
                type="button"
              >
                Site admin
                <ChevronDownIcon aria-hidden="true" className="h-4 w-4" />
              </button>
              {adminMenuOpen && (
                <ul
                  className="menu absolute right-0 z-50 mt-3 w-52 rounded-xl border border-base-300 bg-base-100 p-2 shadow-lg"
                  id="site-admin-navigation"
                >
                  <li>
                    <Link
                      activeOptions={{ exact: true }}
                      activeProps={{ className: 'font-semibold text-primary' }}
                      onClick={() => setAdminMenuOpen(false)}
                      to="/admin"
                    >
                      Admin home
                    </Link>
                  </li>
                  <li>
                    <Link
                      activeProps={{ className: 'font-semibold text-primary' }}
                      onClick={() => setAdminMenuOpen(false)}
                      to="/admin/users"
                    >
                      Admin users
                    </Link>
                  </li>
                  <li>
                    <Link
                      activeProps={{ className: 'font-semibold text-primary' }}
                      onClick={() => setAdminMenuOpen(false)}
                      to="/admin/teams"
                    >
                      Teams
                    </Link>
                  </li>
                </ul>
              )}
            </div>
          )}
        </nav>
      </div>
    </header>
  );
}
