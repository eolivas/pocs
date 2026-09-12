import { usePortalDashboard } from '../api';

/** Minimal dashboard panel demonstrating the BFF composition read path. */
export function DashboardPanel() {
  const { data, isPending, isError, error } = usePortalDashboard();

  if (isPending) {
    return <p aria-busy="true">Loading your dashboard…</p>;
  }

  if (isError) {
    return <div role="alert">Unable to load the dashboard: {error.message}</div>;
  }

  return (
    <section>
      <h2>Borrower Dashboard</h2>
      <p>
        Served by <strong>{data.service}</strong> (status: {data.status}).
      </p>
      <p>Composed from: {data.composesFrom.join(', ')}</p>
    </section>
  );
}
