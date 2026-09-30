import { useUser } from '@/shared/auth/UserContext.tsx';
import { createFileRoute, Link } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/')({
  component: RouteComponent,
});

function RouteComponent() {
  const user = useUser();

  return (
    <main className="container mx-auto max-w-4xl px-4 py-16">
      <img alt="UC Davis College of Agricultural and Environmental Sciences" height={77} src="/caes.svg" width={419} />
      <h1 className="mt-12 text-5xl font-bold">Booking</h1>
      <p className="mt-6 text-xl">Welcome, {user.name}.</p>
      <p className="mt-2 text-base-content/70">
        Booking is in development. Application features will be added here.
      </p>
      <nav aria-label="Account" className="mt-6 flex gap-4">
        <Link className="link" to="/me">Your profile</Link>
        <Link className="link" to="/about">About Booking</Link>
      </nav>
      <section className="mt-12 border-t border-base-300 pt-8">
        <h2 className="text-2xl font-semibold">Development examples</h2>
        <p className="mt-2 text-base-content/70">
          These pages came with the starter and use sample data.
        </p>
        <nav aria-label="Development examples" className="mt-4 flex flex-wrap gap-4">
          <Link className="link" to="/fetch">Data table</Link>
          <Link className="link" to="/table-export">CSV export</Link>
          <Link className="link" to="/form">Forms</Link>
          <Link className="link" to="/styles">Style guide</Link>
          <Link className="link" to="/notification">Notifications</Link>
        </nav>
      </section>
    </main>
  );
}
