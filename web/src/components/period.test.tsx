import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { useMonth } from './period';
import { toApiError } from '../api/client';

function Show() {
  const { from, to } = useMonth();
  return <span>{`${from}..${to}`}</span>;
}

describe('useMonth', () => {
  it.each([
    ['2026-02', '2026-02-01..2026-02-28'],
    ['2028-02', '2028-02-01..2028-02-29'],
    ['2026-12', '2026-12-01..2026-12-31'],
  ])('%s covers the whole month', (month, expected) => {
    render(
      <MemoryRouter initialEntries={[`/?month=${month}`]}>
        <Show />
      </MemoryRouter>,
    );
    expect(screen.getByText(expected)).toBeInTheDocument();
  });
});

describe('toApiError', () => {
  it('prefers the problem detail and keeps the error code', () => {
    const error = toApiError(409, { title: 'Run.AlreadyActive', detail: 'A run is already active.' });
    expect([error.status, error.code, error.message]).toEqual([409, 'Run.AlreadyActive', 'A run is already active.']);
  });

  it('flattens validation errors', () => {
    expect(toApiError(400, { title: 'Bad', errors: { request: ['tenantId is required.'] } }).message).toBe('tenantId is required.');
  });

  it('explains a bare 403', () => {
    expect(toApiError(403, undefined).message).toMatch(/permission/);
  });
});
