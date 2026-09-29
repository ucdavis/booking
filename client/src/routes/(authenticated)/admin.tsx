import { ShieldCheckIcon } from '@heroicons/react/24/outline';
import { createFileRoute, Link, redirect, type ErrorComponentProps } from '@tanstack/react-router';
import { fetchJson, HttpError } from '@/lib/api.ts';

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
  component: SiteAdminPage,
  errorComponent: SiteAdminError,
});

function SiteAdminPage() {
  return (
    <main className="content-container py-4 sm:py-8">
      <nav aria-label="Breadcrumb" className="mb-5 text-sm text-base-content/65">
        <Link className="hover:text-primary hover:underline" to="/temp">Home</Link>{' '}
        <span aria-hidden="true">/</span>{' '}
        <span aria-current="page">Site admin</span>
      </nav>

      <div className="flex items-center gap-3 text-primary">
        <ShieldCheckIcon aria-hidden="true" className="h-6 w-6" />
        <p className="text-sm font-semibold tracking-wide uppercase">Grove administration</p>
      </div>
      <h1 className="mt-3 text-3xl font-semibold tracking-tight text-primary sm:text-4xl">
        Site administration
      </h1>
      <p className="mt-3 max-w-2xl text-lg text-base-content/70">
        Welcome to the administration area for Grove.
      </p>

      <section aria-labelledby="site-access-heading" className="mt-10 max-w-3xl rounded-xl border border-base-300 bg-base-200 p-6 sm:p-8">
        <span className="badge badge-outline text-primary">Site administrator</span>
        <h2 className="mt-4 text-xl font-semibold text-primary" id="site-access-heading">
          Access across Grove
        </h2>
        <p className="mt-3 text-base-content/70">
          This area is reserved for site administrators. Team administrators manage
          their own teams and resources; team permissions do not grant access here.
        </p>
      </section>
    </main>
  );
}

function SiteAdminError({ error }: ErrorComponentProps<unknown>) {
  const forbidden = error instanceof HttpError && error.status === 403;

  return (
    <main className="content-container py-12 sm:py-16">
      <section className="max-w-2xl">
        <h1 className="text-3xl font-semibold text-primary">
          {forbidden ? 'Site admin access required' : 'We could not load site administration'}
        </h1>
        <p className="mt-4 text-base-content/70">
          {forbidden
            ? 'This page is available only to Grove site administrators. Team administrator permissions do not grant access.'
            : 'Refresh the page or try again later.'}
        </p>
        <Link className="btn btn-primary mt-6" to="/temp">Back to Grove</Link>
      </section>
    </main>
  );
}
