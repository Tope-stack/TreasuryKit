import { useMutation, useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { api, unwrap } from '../api/client';
import { useSession } from '../auth/session';
import { MatchCard } from '../components/MatchCard';
import { useInvalidateRecon } from '../components/invalidate';
import { useMonth } from '../components/period';
import { Empty, ErrorBanner, TextPrompt } from '../components/ui';

/** Confirmed matches; an Approver can reverse one, returning its items to the pool. */
export function MatchedPage() {
  const { accountId = '' } = useParams();
  const { from, to } = useMonth();
  const { can } = useSession();
  const invalidate = useInvalidateRecon(accountId);

  const matched = useQuery({
    queryKey: ['review-queue', accountId, 'Confirmed', from, to],
    queryFn: () => unwrap(api.GET('/api/recon/review-queue', { params: { query: { accountId, status: 'Confirmed', from, to } } })),
  });
  const reverse = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      unwrap(api.POST('/api/recon/matches/{id}/reverse', { params: { path: { id } }, body: { reason } })),
    onSettled: invalidate,
  });

  return (
    <div className="stack">
      <ErrorBanner error={matched.error ?? reverse.error} />
      {matched.isSuccess && matched.data.length === 0 && <Empty>No confirmed matches this month.</Empty>}
      {matched.data?.map((details) => (
        <MatchCard
          key={details.match.id}
          details={details}
          actions={
            can('Approver') && (
              <TextPrompt label="Reason" action="Reverse" onSubmit={(reason) => reverse.mutate({ id: details.match.id, reason })} />
            )
          }
        />
      ))}
    </div>
  );
}
