import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, unwrap } from '../api/client';
import { useSession } from '../auth/session';
import { MatchCard } from '../components/MatchCard';
import { useInvalidateRecon } from '../components/invalidate';
import { useMonth } from '../components/period';
import { Empty, ErrorBanner, Notice, TextPrompt } from '../components/ui';

/** Proposed matches awaiting an Approver (plan §5.5). */
export function ReviewPage() {
  const { accountId = '' } = useParams();
  const { from, to } = useMonth();
  const { can, session } = useSession();
  const invalidate = useInvalidateRecon(accountId);
  const client = useQueryClient();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const approver = can('Approver');

  const queue = useQuery({
    queryKey: ['review-queue', accountId, 'Proposed', from, to] as const,
    queryFn: () => unwrap(api.GET('/api/recon/review-queue', { params: { query: { accountId, status: 'Proposed', from, to } } })),
    refetchInterval: 10_000, // other users work the same queue
  });

  const confirm = useMutation({
    mutationFn: (id: string) => unwrap(api.POST('/api/recon/matches/{id}/confirm', { params: { path: { id } } })),
    onSuccess: (_, id) => dropReviewed([id]),
    onSettled: invalidate,
  });
  const queueKey = ['review-queue', accountId, 'Proposed', from, to];
  // A reviewed match leaves the queue and the selection at once, so a stale card can't be bulk-confirmed.
  const dropReviewed = (ids: string[]) => {
    client.setQueryData<typeof queue.data>(queueKey, (current) => current?.filter((d) => !ids.includes(d.match.id)));
    setSelected((s) => new Set([...s].filter((id) => !ids.includes(id))));
  };
  const reject = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      unwrap(api.POST('/api/recon/matches/{id}/reject', { params: { path: { id } }, body: { reason: reason || null } })),
    onSuccess: (_, { id }) => dropReviewed([id]),
    onSettled: invalidate,
  });
  const bulk = useMutation({
    mutationFn: () => unwrap(api.POST('/api/recon/matches/confirm-batch', { body: { matchIds: [...selected] } })),
    onSuccess: (result) => dropReviewed(result.confirmed),
    onSettled: invalidate,
  });

  const items = queue.data ?? [];
  // Manual matches the current user proposed can't be confirmed by them (maker-checker); don't offer it.
  const ownManual = (proposedBy: string, rule: string) => rule === 'Manual' && proposedBy === session?.userId;

  return (
    <div className="stack">
      <ErrorBanner error={queue.error ?? confirm.error ?? reject.error ?? bulk.error} />
      {bulk.data && bulk.data.failed.length > 0 && (
        <div role="alert" className="banner error">
          {bulk.data.confirmed.length} confirmed; {bulk.data.failed.length} not confirmed:{' '}
          {bulk.data.failed.map((f) => f.message).join(' ')}
        </div>
      )}
      {bulk.data && bulk.data.failed.length === 0 && <Notice>{bulk.data.confirmed.length} matches confirmed.</Notice>}

      {approver && items.length > 0 && (
        <div className="toolbar">
          <label className="check">
            <input
              type="checkbox"
              checked={selected.size > 0 && selected.size === items.length}
              onChange={(e) => setSelected(e.target.checked ? new Set(items.map((i) => i.match.id)) : new Set())}
            />
            Select all
          </label>
          <button type="button" disabled={selected.size === 0 || bulk.isPending} onClick={() => bulk.mutate()}>
            Confirm selected ({selected.size})
          </button>
        </div>
      )}

      {queue.isSuccess && items.length === 0 && <Empty>No proposals to review for this month.</Empty>}

      {items.map((details) => {
        const id = details.match.id;
        const own = ownManual(details.match.proposedBy, details.match.ruleKey);
        return (
          <MatchCard
            key={id}
            details={details}
            select={
              approver && (
                <input
                  type="checkbox"
                  aria-label="Select match"
                  checked={selected.has(id)}
                  onChange={(e) =>
                    setSelected((s) => {
                      const next = new Set(s);
                      if (e.target.checked) next.add(id);
                      else next.delete(id);
                      return next;
                    })
                  }
                />
              )
            }
            actions={
              approver && (
                <>
                  <button type="button" onClick={() => confirm.mutate(id)} disabled={confirm.isPending} title={own ? 'You proposed this match; another Approver must confirm it.' : undefined}>
                    Confirm
                  </button>
                  <TextPrompt label="Reason (optional)" action="Reject" optional onSubmit={(reason) => reject.mutate({ id, reason })} />
                  {own && <span className="muted">You proposed this — another Approver must confirm it.</span>}
                </>
              )
            }
          />
        );
      })}
    </div>
  );
}
