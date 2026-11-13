import { useMutation, useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { api, unwrap } from '../api/client';
import type { CandidateDetail, ExceptionDetails } from '../api/types';
import { num } from '../api/types';
import { useSession } from '../auth/session';
import { ItemTable } from '../components/items';
import { useInvalidateRecon } from '../components/invalidate';
import { Badge, Empty, ErrorBanner, Money, Notice, TextPrompt } from '../components/ui';

/** Open exceptions with their closest candidates; a Preparer can match, resolve or ignore them (plan §5.5). */
export function ExceptionsPage() {
  const { accountId = '' } = useParams();
  const { can } = useSession();
  const invalidate = useInvalidateRecon(accountId);
  const preparer = can('Preparer');

  const queue = useQuery({
    queryKey: ['exception-queue', accountId],
    queryFn: () => unwrap(api.GET('/api/recon/exception-queue', { params: { query: { accountId, status: 'Open' } } })),
    refetchInterval: 10_000, // other users work the same queue
  });

  const propose = useMutation({
    mutationFn: ({ exception, candidate }: { exception: ExceptionDetails; candidate: CandidateDetail }) => {
      const pair = [exception.item!, candidate.item!];
      return unwrap(
        api.POST('/api/recon/matches', {
          body: {
            statementLineIds: pair.filter((i) => i.type === 'StatementLine').map((i) => i.id),
            expectedTransactionIds: pair.filter((i) => i.type === 'ExpectedTransaction').map((i) => i.id),
            reason: 'Matched from exception queue',
          },
        }),
      );
    },
    onSettled: invalidate,
  });
  const close = useMutation({
    mutationFn: ({ id, action, note }: { id: string; action: 'resolve' | 'ignore'; note: string }) =>
      action === 'resolve'
        ? unwrap(api.POST('/api/recon/exceptions/{id}/resolve', { params: { path: { id } }, body: { note } }))
        : unwrap(api.POST('/api/recon/exceptions/{id}/ignore', { params: { path: { id } }, body: { note } })),
    onSettled: invalidate,
  });

  const items = queue.data ?? [];
  return (
    <div className="stack">
      <ErrorBanner error={queue.error ?? propose.error ?? close.error} />
      {propose.isSuccess && <Notice>Manual match proposed. An Approver must confirm it in Review.</Notice>}
      {queue.isSuccess && items.length === 0 && <Empty>No open exceptions.</Empty>}

      {items.map((x) => (
        <article key={x.exception.id} className="card" data-testid="exception-card" aria-label={`Exception ${x.exception.id}`}>
          <header className="match-head">
            <Badge tone="warn">{x.exception.reason}</Badge>
            <strong>{x.exception.itemType === 'StatementLine' ? 'Bank statement line' : 'Expected transaction'}</strong>
          </header>
          {x.item && <ItemTable caption="Unmatched item" items={[x.item]} />}

          {x.candidates.length > 0 && (
            <table className="candidates">
              <caption>Closest candidates</caption>
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Description</th>
                  <th scope="col" className="num">
                    Amount
                  </th>
                  <th scope="col" className="num">
                    Score
                  </th>
                  {preparer && <th scope="col">Action</th>}
                </tr>
              </thead>
              <tbody>
                {x.candidates.map(
                  (c) =>
                    c.item && (
                      <tr key={c.item.id}>
                        <td>{c.item.date}</td>
                        <td>
                          {c.item.description || '—'}
                          {c.item.reference && <div className="muted">{c.item.reference}</div>}
                        </td>
                        <td className="num">
                          <Money value={c.item.amount} />
                        </td>
                        <td className="num">{num(c.score).toFixed(2)}</td>
                        {preparer && (
                          <td>
                            <button type="button" className="secondary" disabled={propose.isPending} onClick={() => propose.mutate({ exception: x, candidate: c })}>
                              Match with this
                            </button>
                          </td>
                        )}
                      </tr>
                    ),
                )}
              </tbody>
            </table>
          )}

          {preparer && (
            <footer className="actions">
              <TextPrompt label="Note" action="Resolve" onSubmit={(note) => close.mutate({ id: x.exception.id, action: 'resolve', note })} />
              <TextPrompt label="Note" action="Ignore" onSubmit={(note) => close.mutate({ id: x.exception.id, action: 'ignore', note })} />
            </footer>
          )}
        </article>
      ))}
    </div>
  );
}
