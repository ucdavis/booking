import { ChevronDownIcon } from '@heroicons/react/24/outline';
import { useMutation } from '@tanstack/react-query';
import { Link, useLocation, useRouter } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';
import { EmulateUserDialog } from '@/features/admin/EmulateUserDialog.tsx';
import { TeamAdminMenu } from '@/features/teams/TeamAdminMenu.tsx';
import { fetchJson } from '@/lib/api.ts';
import {
  emulationErrorMessage,
  stopEmulation,
  subscribeToEmulationChanges,
} from '@/queries/emulation.ts';
import { useMeQuery } from '@/queries/user.ts';

const navigationItems = [
  ['Find resources', '/temp/resources'],
  ['My reservations', '/temp/reservations'],
  ['Manage resources', '/temp/admin/resources'],
] as const;

export function SiteHeader() {
  const userQuery = useMeQuery();
  const router = useRouter();
  const pathname = useLocation({ select: (location) => location.pathname });
  const [adminMenuOpen, setAdminMenuOpen] = useState(false);
  const adminMenuRef = useRef<HTMLDivElement>(null);
  const adminButtonRef = useRef<HTMLButtonElement>(null);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);
  const userButtonRef = useRef<HTMLButtonElement>(null);
  const [emulationDialogOpen, setEmulationDialogOpen] = useState(false);
  const isAdminPage = pathname === '/admin' || pathname.startsWith('/admin/');
  const stopEmulationMutation = useMutation({
    mutationFn: stopEmulation,
    onSuccess: () => router.navigate({ href: '/temp', reloadDocument: true }),
    retry: false,
  });
  const isStoppingEmulation =
    stopEmulationMutation.isPending || stopEmulationMutation.isSuccess;
  const logoutMutation = useMutation({
    mutationFn: async () => {
      const { formFieldName, requestToken } = await fetchJson<{
        formFieldName: string;
        requestToken: string;
      }>('/logout/antiforgery', {
        cache: 'no-store',
        skipRedirectOn401: true,
      });
      // Submit a document navigation so Microsoft sign-out can redirect the browser.
      const form = document.createElement('form');
      form.method = 'post';
      form.action = '/logout';
      form.hidden = true;
      const token = document.createElement('input');
      token.type = 'hidden';
      token.name = formFieldName;
      token.value = requestToken;
      form.append(token);
      document.body.append(form);
      try {
        form.submit();
      } catch (error) {
        form.remove();
        throw error;
      }
    },
  });

  useEffect(
    () =>
      subscribeToEmulationChanges(() => {
        void router.navigate({ href: '/temp', reloadDocument: true });
      }),
    [router]
  );

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

  useEffect(() => {
    if (!userMenuOpen) {
      return;
    }

    const closeOnOutsideClick = (event: PointerEvent) => {
      if (!userMenuRef.current?.contains(event.target as Node | null)) {
        setUserMenuOpen(false);
      }
    };
    document.addEventListener('pointerdown', closeOnOutsideClick);
    return () =>
      document.removeEventListener('pointerdown', closeOnOutsideClick);
  }, [userMenuOpen]);

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
          className="flex w-full flex-wrap items-center gap-x-6 gap-y-2 text-sm font-medium sm:w-auto"
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
          {userQuery.isSuccess && (
            <div
              className="relative ml-auto"
              onBlur={(event) => {
                if (!event.currentTarget.contains(event.relatedTarget)) {
                  setUserMenuOpen(false);
                }
              }}
              onKeyDown={(event) => {
                if (event.key === 'Escape') {
                  setUserMenuOpen(false);
                  userButtonRef.current?.focus();
                }
              }}
              ref={userMenuRef}
            >
              <button
                aria-controls="user-account-navigation"
                aria-expanded={userMenuOpen}
                aria-label={
                  userQuery.data.isEmulating
                    ? `${userQuery.data.name} (Emulating)`
                    : undefined
                }
                className={`flex items-center gap-1.5 transition-colors hover:text-primary ${pathname === '/me' ? 'font-semibold text-primary' : 'text-base-content/70'}`}
                onClick={() => setUserMenuOpen((open) => !open)}
                ref={userButtonRef}
                type="button"
              >
                <span
                  className="max-w-48 truncate sm:max-w-64"
                  title={userQuery.data.name}
                >
                  {userQuery.data.name}
                </span>
                {userQuery.data.isEmulating && (
                  <span className="shrink-0 font-semibold text-primary">
                    (Emulating)
                  </span>
                )}
                <ChevronDownIcon
                  aria-hidden="true"
                  className="h-4 w-4 shrink-0"
                />
              </button>
              {userMenuOpen && (
                <ul
                  className="menu absolute right-0 z-50 mt-3 w-52 max-w-[calc(100vw-2rem)] rounded-xl border border-base-300 bg-base-100 p-2 shadow-lg"
                  id="user-account-navigation"
                >
                  <li>
                    <Link
                      activeProps={{ className: 'font-semibold text-primary' }}
                      onClick={() => setUserMenuOpen(false)}
                      to="/me"
                    >
                      Profile
                    </Link>
                  </li>
                  {userQuery.data.isSiteAdmin &&
                    !userQuery.data.isEmulating && (
                      <li>
                        <button
                          disabled={
                            logoutMutation.isPending || logoutMutation.isSuccess
                          }
                          onClick={() => {
                            setUserMenuOpen(false);
                            setEmulationDialogOpen(true);
                          }}
                          type="button"
                        >
                          Emulate user
                        </button>
                      </li>
                    )}
                  {userQuery.data.isEmulating && (
                    <li>
                      <button
                        disabled={
                          isStoppingEmulation ||
                          logoutMutation.isPending ||
                          logoutMutation.isSuccess
                        }
                        onClick={() => stopEmulationMutation.mutate()}
                        type="button"
                      >
                        {isStoppingEmulation
                          ? 'Stopping emulation…'
                          : 'Stop emulating'}
                      </button>
                    </li>
                  )}
                  <li>
                    <button
                      disabled={
                        logoutMutation.isPending ||
                        logoutMutation.isSuccess ||
                        isStoppingEmulation
                      }
                      onClick={() => logoutMutation.mutate()}
                      type="button"
                    >
                      {logoutMutation.isPending || logoutMutation.isSuccess
                        ? 'Logging out…'
                        : 'Log out'}
                    </button>
                  </li>
                  {logoutMutation.isError && (
                    <li>
                      <p className="text-error" role="alert">
                        Unable to log out. Please try again.
                      </p>
                    </li>
                  )}
                  {stopEmulationMutation.isError && (
                    <li>
                      <p className="text-error" role="alert">
                        {emulationErrorMessage(
                          stopEmulationMutation.error,
                          'Unable to stop emulating. Please try again.'
                        )}
                      </p>
                    </li>
                  )}
                </ul>
              )}
            </div>
          )}
        </nav>
      </div>
      {emulationDialogOpen &&
        userQuery.isSuccess &&
        userQuery.data.isSiteAdmin &&
        !userQuery.data.isEmulating && (
          <EmulateUserDialog
            onClose={() => setEmulationDialogOpen(false)}
            returnFocusRef={userButtonRef}
          />
        )}
    </header>
  );
}
