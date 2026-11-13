import { useQueryClient } from '@tanstack/react-query';

/** After any match or exception change, the queues, summary and expected transactions are stale. */
export function useInvalidateRecon(accountId: string) {
  const client = useQueryClient();
  return () =>
    Promise.all(
      [['review-queue', accountId], ['exception-queue', accountId], ['summary', accountId], ['runs', accountId]].map((queryKey) =>
        client.invalidateQueries({ queryKey }),
      ),
    );
}
