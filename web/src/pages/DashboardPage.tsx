import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { api, download, unwrap } from '../api/client';
import { num } from '../api/types';
import { useSession } from '../auth/session';
import { useMonth } from '../components/period';
import { Badge, ErrorBanner, Money, Notice, statusTone } from '../components/ui';

export function DashboardPage() {
  const { accountId = '' } = useParams();
  const { from, to } = useMonth();
  const { can } = useSession();
  const client = useQueryClient();

  // Runs start in the background (e.g. right after a statement import), so keep watching for them.
  const runs = useQuery({
    queryKey: ['runs', accountId],
    queryFn: () => unwrap(api.GET('/api/recon/runs', { params: { query: { accountId } } })),
    refetchInterval: (q) => (q.state.data?.some((r) => r.status === 'Queued' || r.status === 'Running') ? 1500 : 10_000),
  });
  const latestRun = runs.data?.[0];
  // Keyed on the latest run, so the figures refresh whenever a run starts or finishes.
  const summary = useQuery({
    queryKey: ['summary', accountId, from, to, latestRun?.id, latestRun?.status],
    queryFn: () => unwrap(api.GET('/api/recon/reports/summary', { params: { query: { accountId, from, to } } })),
  });
  const run = useMutation({
    mutationFn: () => unwrap(api.POST('/api/recon/runs', { body: { accountId, from, to } })),
    onSuccess: () => client.invalidateQueries({ queryKey: ['runs', accountId] }),
  });
  const exportFile = useMutation({
    mutationFn: (format: 'csv' | 'xlsx') =>
      download(`/api/recon/reports/export?accountId=${accountId}&from=${from}&to=${to}&format=${format}`),
  });

  const s = summary.data;
  const monthReconciled = runs.data?.some((r) => r.status === 'Completed' && r.periodStart <= to && r.periodEnd >= from) ?? false;
  const openExceptions = s ? Object.values(s.openExceptionsByReason).reduce<number>((a, b) => a + num(b), 0) : 0;
  const pendingWork = s ? num(s.pendingProposals) + openExceptions : undefined;

  return (
    <div className="stack">
      <ErrorBanner error={summary.error ?? run.error ?? exportFile.error} />
      <div className="toolbar">
        {can('Preparer') && (
          <button type="button" onClick={() => run.mutate()} disabled={run.isPending}>
            Run reconciliation
          </button>
        )}
        <button type="button" className="secondary" onClick={() => exportFile.mutate('csv')}>
          Export CSV
        </button>
        <button type="button" className="secondary" onClick={() => exportFile.mutate('xlsx')}>
          Export XLSX
        </button>
        {latestRun && (
          <span className="muted">
            Last run <Badge tone={statusTone(latestRun.status)}>{latestRun.status}</Badge> {latestRun.periodStart} – {latestRun.periodEnd}
          </span>
        )}
      </div>

      {s && monthReconciled && pendingWork === 0 && num(s.lines.total) > 0 && <Notice>Reconciliation complete: no pending proposals or open exceptions.</Notice>}

      {s && (
        <div className="kpis">
          <Kpi label="Matched (count)" value={`${num(s.lines.matchedPercentByCount).toFixed(1)}%`} detail={`${s.lines.matched} of ${s.lines.total} lines`} />
          <Kpi label="Matched (amount)" value={`${num(s.lines.matchedPercentByAmount).toFixed(1)}%`} detail={<Money value={s.lines.matchedAmount} />} />
          <Kpi label="Pending proposals" value={String(s.pendingProposals)} testId="kpi-pending" />
          <Kpi
            label="Open exceptions"
            value={String(openExceptions)}
            detail={Object.entries(s.openExceptionsByReason).map(([k, v]) => `${k}: ${v}`).join(' · ') || '—'}
            testId="kpi-exceptions"
          />
          <Kpi label="Unmatched inflows" value={String(s.lines.unmatchedInflowCount)} detail={<Money value={s.lines.unmatchedInflowAmount} />} />
          <Kpi label="Unmatched outflows" value={String(s.lines.unmatchedOutflowCount)} detail={<Money value={s.lines.unmatchedOutflowAmount} />} />
          <Kpi label="Expected, not yet received" value={String(s.expected.unmatchedInflowCount)} detail={<Money value={s.expected.unmatchedInflowAmount} />} />
          <Kpi label="Expected, not yet paid" value={String(s.expected.unmatchedOutflowCount)} detail={<Money value={s.expected.unmatchedOutflowAmount} />} />
        </div>
      )}

      {s && (
        <table>
          <caption>Unmatched statement lines by age</caption>
          <thead>
            <tr>
              <th scope="col">Age (days)</th>
              <th scope="col" className="num">
                Lines
              </th>
              <th scope="col" className="num">
                Amount
              </th>
            </tr>
          </thead>
          <tbody>
            {s.unmatchedLineAging.map((b) => (
              <tr key={b.bucket}>
                <td>{b.bucket}</td>
                <td className="num">{b.count}</td>
                <td className="num">
                  <Money value={b.amount} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

function Kpi({ label, value, detail, testId }: { label: string; value: string; detail?: React.ReactNode; testId?: string }) {
  return (
    <div className="kpi" data-testid={testId}>
      <div className="kpi-label">{label}</div>
      <div className="kpi-value">{value}</div>
      {detail && <div className="muted">{detail}</div>}
    </div>
  );
}
