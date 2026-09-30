import { Link } from '@tanstack/react-router';

export function NotAuthorizedPage({
  description = "You don't have permission to view this page.",
}: {
  description?: string;
}) {
  return (
    <main className="content-container py-12 sm:py-16">
      <section className="max-w-2xl">
        <h1 className="text-3xl font-semibold text-primary">Not authorized</h1>
        <p className="mt-4 text-base-content/70">{description}</p>
        <Link className="btn btn-primary mt-6" to="/temp">
          Back to Booking
        </Link>
      </section>
    </main>
  );
}
