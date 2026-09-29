import {
  createFileRoute,
  Link,
  Outlet,
  redirect,
  type ErrorComponentProps,
} from '@tanstack/react-router';
import { fetchJson, HttpError } from '@/lib/api.ts';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';

export const Route = createFileRoute('/(authenticated)/admin')({
  beforeLoad: async ({ location }) => {
    try {
      await fetchJson<void>('/api/admin/access', {
        cache: 'no-store',
        skipRedirectOn401: true,
      });
    } catch (error) {
      if (error instanceof HttpError && error.status === 401) {
        throw redirect({
          href: `/login?returnUrl=${encodeURIComponent(location.href)}`,
          reloadDocument: true,
        });
      }
      throw error;
    }
  },
  component: Outlet,
  errorComponent: SiteAdminError,
});

function SiteAdminError({ error }: ErrorComponentProps<unknown>) {
  if (error instanceof HttpError && error.status === 403) {
    return <NotAuthorizedPage />;
  }

  return (
    <main className="content-container py-12 sm:py-16">
      <section className="max-w-2xl">
        <h1 className="text-3xl font-semibold text-primary">
          We could not load site administration
        </h1>
        <p className="mt-4 text-base-content/70">
          Refresh the page or try again later.
        </p>
        <Link className="btn btn-primary mt-6" to="/temp">
          Back to Grove
        </Link>
      </section>
    </main>
  );
}
