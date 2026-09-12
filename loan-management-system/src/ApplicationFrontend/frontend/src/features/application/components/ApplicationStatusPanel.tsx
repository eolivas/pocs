import { useApplicationStatus } from '../api';

/** Minimal panel demonstrating the Application BFF composition read path. */
export function ApplicationStatusPanel() {
  const { data, isPending, isError, error } = useApplicationStatus();

  if (isPending) {
    return <p aria-busy="true">Loading your application…</p>;
  }

  if (isError) {
    return <div role="alert">Unable to load application status: {error.message}</div>;
  }

  return (
    <section>
      <h2>Loan Application</h2>
      <p>
        Served by the <strong>{data.bff}</strong> BFF (status: {data.status}).
      </p>
      <p>Composed from: {data.composesFrom.join(', ')}</p>
    </section>
  );
}
