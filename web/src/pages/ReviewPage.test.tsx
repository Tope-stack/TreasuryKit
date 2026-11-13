import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { matchDetails, mockApi, renderAt, signedInAs } from '../test/render';
import { ReviewPage } from './ReviewPage';

const url = '/accounts/a1/review?month=2026-02';
const path = '/accounts/:accountId/review';

describe('ReviewPage', () => {
  beforeEach(() => sessionStorage.clear());

  it('shows both sides of a proposal with its rule, confidence and score breakdown', async () => {
    signedInAs('amy', ['Approver']);
    mockApi({ 'GET /api/recon/review-queue': () => [matchDetails({ ruleKey: 'Tolerance', confidence: 0.85, adjustment: -52.5 })] });

    renderAt(<ReviewPage />, { url, path });

    const card = await screen.findByTestId('match-card');
    expect(within(card).getByText('Tolerance')).toBeInTheDocument();
    expect(within(card).getByText(/confidence 0\.85/)).toBeInTheDocument();
    expect(within(card).getByText('Bank statement')).toBeInTheDocument();
    expect(within(card).getByText('Invoice INV-1')).toBeInTheDocument();
    expect(within(card).getByText(/Adjustment/)).toHaveTextContent('-52.50');
    expect(within(card).getByLabelText('Score breakdown')).toHaveTextContent('base0.8');
  });

  it('hides approval actions from roles that cannot approve', async () => {
    signedInAs('vic', ['Viewer']);
    mockApi({ 'GET /api/recon/review-queue': () => [matchDetails()] });

    renderAt(<ReviewPage />, { url, path });

    await screen.findByTestId('match-card');
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Confirm selected/ })).not.toBeInTheDocument();
  });

  it('asks for the queue of the selected month', async () => {
    signedInAs('amy', ['Approver']);
    const calls = mockApi({ 'GET /api/recon/review-queue': () => [] });

    renderAt(<ReviewPage />, { url, path });

    await screen.findByText(/No proposals/);
    const request = new URL(`http://x${calls[0]!.path}`);
    expect(request.pathname).toBe('/api/recon/review-queue');
  });

  it('warns the proposer of a manual match that someone else must confirm it', async () => {
    signedInAs('pat', ['Preparer', 'Approver']);
    mockApi({ 'GET /api/recon/review-queue': () => [matchDetails({ ruleKey: 'Manual', proposedBy: 'pat' })] });

    renderAt(<ReviewPage />, { url, path });

    expect(await screen.findByText(/another Approver must confirm it/)).toBeInTheDocument();
  });

  it('shows the maker-checker refusal from the API', async () => {
    signedInAs('pat', ['Approver']);
    const proposal = matchDetails({ ruleKey: 'Manual', proposedBy: 'someone' });
    mockApi({
      'GET /api/recon/review-queue': () => [proposal],
      [`POST /api/recon/matches/${proposal.match.id}/confirm`]: {
        status: 403,
        body: { title: 'Match.MakerChecker', detail: 'A manual match must be confirmed by a different user from the one who proposed it.' },
      },
    });

    renderAt(<ReviewPage />, { url, path });
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('proposed it. (Match.MakerChecker)');
  });

  it('bulk-confirms the selected matches and reports failures per match', async () => {
    signedInAs('amy', ['Approver']);
    const [a, b] = [matchDetails(), matchDetails()];
    const calls = mockApi({
      'GET /api/recon/review-queue': () => [a, b],
      'POST /api/recon/matches/confirm-batch': () => ({
        confirmed: [a.match.id],
        failed: [{ matchId: b.match.id, code: 'Match.ConcurrentChange', message: 'Changed by someone else.' }],
      }),
    });

    renderAt(<ReviewPage />, { url, path });
    await screen.findAllByTestId('match-card');
    await userEvent.click(screen.getByLabelText('Select all'));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm selected (2)' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('1 confirmed; 1 not confirmed: Changed by someone else.'));
    const batch = calls.find((c) => c.path === '/api/recon/matches/confirm-batch');
    expect(batch?.body).toEqual({ matchIds: [a.match.id, b.match.id] });
  });
});
