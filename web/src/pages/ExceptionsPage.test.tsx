import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { item, mockApi, renderAt, signedInAs } from '../test/render';
import { ExceptionsPage } from './ExceptionsPage';

const line = item({ id: 'line-1', amount: -150000, description: 'NIP TRF TO IKEJA OFFICE MART' });
const candidate = item({ id: 'exp-1', type: 'ExpectedTransaction', amount: -150000, description: 'Supplier payment due', reference: null });
const exception = {
  exception: { id: 'x1', accountId: 'a1', runId: 'r1', itemType: 'StatementLine', itemId: 'line-1', reason: 'NoCandidate', candidates: [], status: 'Open', createdAt: '2026-02-20T00:00:00Z' },
  item: line,
  candidates: [{ score: 0.93, scoreBreakdown: { score: 0.93 }, item: candidate }],
};

describe('ExceptionsPage', () => {
  beforeEach(() => sessionStorage.clear());

  it('proposes a manual match with each item on its own side', async () => {
    signedInAs('pat', ['Preparer']);
    const calls = mockApi({
      'GET /api/recon/exception-queue': () => [exception],
      'POST /api/recon/matches': () => ({ matchId: 'm1' }),
    });

    renderAt(<ExceptionsPage />, { url: '/accounts/a1/exceptions', path: '/accounts/:accountId/exceptions' });
    await userEvent.click(await screen.findByRole('button', { name: 'Match with this' }));

    expect(await screen.findByText(/Manual match proposed/)).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      statementLineIds: ['line-1'],
      expectedTransactionIds: ['exp-1'],
      reason: 'Matched from exception queue',
    });
  });

  it('requires a note to ignore an exception', async () => {
    signedInAs('pat', ['Preparer']);
    const calls = mockApi({ 'GET /api/recon/exception-queue': () => [exception], 'POST /api/recon/exceptions/x1/ignore': () => null });

    renderAt(<ExceptionsPage />, { url: '/accounts/a1/exceptions', path: '/accounts/:accountId/exceptions' });
    await userEvent.click(await screen.findByRole('button', { name: 'Ignore' }));
    const submit = screen.getByRole('button', { name: 'Ignore' });
    expect(submit).toBeDisabled();

    await userEvent.type(screen.getByLabelText('Note'), 'Internal transfer');
    await userEvent.click(submit);

    expect(calls.find((c) => c.path.endsWith('/ignore'))?.body).toEqual({ note: 'Internal transfer' });
  });

  it('is read-only for viewers', async () => {
    signedInAs('vic', ['Viewer']);
    mockApi({ 'GET /api/recon/exception-queue': () => [exception] });

    renderAt(<ExceptionsPage />, { url: '/accounts/a1/exceptions', path: '/accounts/:accountId/exceptions' });

    expect(await screen.findByTestId('exception-card')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Match with this' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Resolve' })).not.toBeInTheDocument();
  });
});
