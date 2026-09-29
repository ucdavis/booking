import {
  createFileRoute,
  Outlet,
  redirect,
  type ErrorComponentProps,
} from '@tanstack/react-router';
import { HttpError } from '../../lib/api.ts';
import { meQueryOptions } from '../../queries/user.ts';
import { UserProvider } from '@/shared/auth/UserContext.tsx';
import { NotAuthorizedPage } from '@/shared/auth/NotAuthorizedPage.tsx';

export const Route = createFileRoute('/(authenticated)')({
  beforeLoad: async ({ context, location }) => {
    try {
      await context.queryClient.ensureQueryData(meQueryOptions());
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
  component: () => (
    <UserProvider>
      <Outlet />
    </UserProvider>
  ),
  errorComponent: AuthenticatedRouteError,
});

function AuthenticatedRouteError({ error }: ErrorComponentProps<unknown>) {
  if (error instanceof HttpError && error.status === 403) {
    return <NotAuthorizedPage />;
  }

  return (
    <main className="min-h-screen flex items-center justify-center px-4 py-12">
      <section className="max-w-lg text-center">
        <h1 className="text-3xl font-bold text-gray-900">
          We could not load this page
        </h1>
        <p className="mt-4 text-gray-600">
          Refresh the page or try again later.
        </p>
      </section>
    </main>
  );
}
