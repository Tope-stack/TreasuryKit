import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { Role } from '../api/types';
import { SessionProvider, type Session } from '../auth/session';

export function signedInAs(userId: string, roles: Role[]): Session {
  const session: Session = {
    token: 'test-token',
    tenantId: 't1',
    tenantName: 'Test Ltd',
    userId,
    roles,
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
  };
  sessionStorage.setItem('treasurykit.session', JSON.stringify(session));
  return session;
}

/** Renders `element` at `url`, matched by `path`, with a fresh query cache and the stored session. */
export function renderAt(element: ReactNode, { url, path }: { url: string; path: string }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <SessionProvider>
        <MemoryRouter initialEntries={[url]}>
          <Routes>
            <Route path={path} element={element} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </QueryClientProvider>,
  );
}

type Handler = (request: Request) => unknown | Promise<unknown>;

/** Stubs fetch with route handlers keyed by "METHOD /path"; records every request. */
export function mockApi(routes: Record<string, Handler | { status: number; body: unknown }>) {
  const calls: { method: string; path: string; body: unknown }[] = [];
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const url = new URL(request.url);
    const text = await request.clone().text();
    calls.push({ method: request.method, path: url.pathname, body: text ? JSON.parse(text) : undefined });
    const route = routes[`${request.method} ${url.pathname}`];
    if (!route) {
      return new Response(JSON.stringify({ title: 'NotMocked', detail: url.pathname }), { status: 404 });
    }
    if (typeof route === 'function') {
      return new Response(JSON.stringify((await route(request)) ?? null), { status: 200, headers: { 'content-type': 'application/json' } });
    }
    return new Response(JSON.stringify(route.body), { status: route.status, headers: { 'content-type': 'application/json' } });
  }) as typeof fetch;
  return calls;
}

export const item = (over: Partial<Record<string, unknown>> = {}) => ({
  type: 'StatementLine',
  id: crypto.randomUUID(),
  date: '2026-02-10',
  amount: 1000,
  currency: 'NGN',
  description: 'NIP/FRM EKO FOODS PLC',
  reference: 'FT1',
  counterparty: 'EKO FOODS PLC',
  allocatedAmount: 1000,
  ...over,
});

export const matchDetails = (over: Partial<Record<string, unknown>> = {}) => ({
  match: {
    id: crypto.randomUUID(),
    accountId: 'a1',
    runId: null,
    type: 'OneToOne',
    ruleKey: 'AmountDateWindow',
    confidence: 0.9,
    scoreBreakdown: { base: 0.8, date: 0.1, name: 0 },
    status: 'Proposed',
    currency: 'NGN',
    valueDate: '2026-02-10',
    proposedBy: 'system',
    proposedAt: '2026-02-11T00:00:00Z',
    confirmedBy: null,
    confirmedAt: null,
    reason: null,
    adjustment: 0,
    reviewedBy: null,
    reviewedAt: null,
    items: [],
    ...over,
  },
  statementItems: [item()],
  expectedItems: [item({ type: 'ExpectedTransaction', description: 'Invoice INV-1', reference: 'INV-1' })],
});
