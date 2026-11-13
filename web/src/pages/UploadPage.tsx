import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { api, unwrap, upload } from '../api/client';
import type { ExpectedImport, StatementImport } from '../api/types';
import { useSession } from '../auth/session';
import { Badge, Empty, ErrorBanner, FilePicker, statusTone } from '../components/ui';

const pending = (status: string) => status === 'Queued' || status === 'Parsing' || status === 'Processing';

export function UploadPage() {
  const { accountId = '' } = useParams();
  const { can } = useSession();
  const client = useQueryClient();

  const imports = useQuery({
    queryKey: ['imports', accountId],
    queryFn: () => unwrap(api.GET('/api/core/accounts/{id}/imports', { params: { path: { id: accountId } } })),
    refetchInterval: (q) => (q.state.data?.some((i) => pending(i.status)) ? 1500 : false),
  });
  const expectedImports = useQuery({
    queryKey: ['expected-imports'],
    queryFn: () => unwrap(api.GET('/api/core/expected-transactions/imports')),
    refetchInterval: (q) => (q.state.data?.some((i) => pending(i.status)) ? 1500 : false),
  });

  const statement = useMutation({
    mutationFn: (file: File) => upload(`/api/core/accounts/${accountId}/statements`, file),
    onSuccess: () => client.invalidateQueries({ queryKey: ['imports', accountId] }),
  });
  const expected = useMutation({
    mutationFn: (file: File) => upload('/api/core/expected-transactions/import', file),
    onSuccess: () => client.invalidateQueries({ queryKey: ['expected-imports'] }),
  });

  return (
    <div className="stack">
      {can('Preparer') && (
        <div className="grid-2 cards">
          <div className="card">
            <h2>Bank statement</h2>
            <p className="muted">CSV/XLSX (using the account's mapping profile), MT940 or CAMT.053. Reconciliation starts automatically.</p>
            <FilePicker label="Statement file" accept=".csv,.xlsx,.sta,.mt940,.940,.txt,.xml" onFile={(f) => statement.mutate(f)} busy={statement.isPending} />
            <ErrorBanner error={statement.error} />
          </div>
          <div className="card">
            <h2>Expected transactions</h2>
            <p className="muted">CSV of payables and receivables. Re-uploading updates rows by externalId.</p>
            <FilePicker label="Expected transactions CSV" accept=".csv" onFile={(f) => expected.mutate(f)} busy={expected.isPending} />
            <ErrorBanner error={expected.error} />
          </div>
        </div>
      )}

      <StatementImports imports={imports.data} />
      <ExpectedImports imports={expectedImports.data} />
    </div>
  );
}

function StatementImports({ imports }: { imports: StatementImport[] | undefined }) {
  if (!imports) return null;
  if (imports.length === 0) return <Empty>No statements imported yet.</Empty>;
  return (
    <table>
      <caption>Statement imports</caption>
      <thead>
        <tr>
          <th scope="col">File</th>
          <th scope="col">Status</th>
          <th scope="col">Period</th>
          <th scope="col" className="num">
            Lines
          </th>
          <th scope="col" className="num">
            Duplicates
          </th>
          <th scope="col">Details</th>
        </tr>
      </thead>
      <tbody>
        {imports.map((i) => (
          <tr key={i.id} data-testid="statement-import">
            <td>{i.fileName}</td>
            <td>
              <Badge tone={statusTone(i.status)}>{i.status}</Badge>
            </td>
            <td>{i.periodStart ? `${i.periodStart} – ${i.periodEnd}` : '—'}</td>
            <td className="num">{i.lineCount}</td>
            <td className="num">{i.duplicateLineCount}</td>
            <td className="muted">{i.error ?? (i.balanceValidated ? 'Balances validated' : '')}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function ExpectedImports({ imports }: { imports: ExpectedImport[] | undefined }) {
  if (!imports || imports.length === 0) return null;
  return (
    <table>
      <caption>Expected-transaction imports</caption>
      <thead>
        <tr>
          <th scope="col">File</th>
          <th scope="col">Status</th>
          <th scope="col" className="num">
            Created
          </th>
          <th scope="col" className="num">
            Updated
          </th>
          <th scope="col" className="num">
            Unchanged
          </th>
          <th scope="col">Errors</th>
        </tr>
      </thead>
      <tbody>
        {imports.map((i) => (
          <tr key={i.id} data-testid="expected-import">
            <td>{i.fileName}</td>
            <td>
              <Badge tone={statusTone(i.status)}>{i.status}</Badge>
            </td>
            <td className="num">{i.created}</td>
            <td className="num">{i.updated}</td>
            <td className="num">{i.unchanged}</td>
            <td className="muted">{i.errors.map((e) => `Row ${e.row}: ${e.message}`).join(' ') || '—'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
