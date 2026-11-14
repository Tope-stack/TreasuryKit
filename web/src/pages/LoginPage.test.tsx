import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { mockApi, renderAt } from '../test/render';
import { LoginPage } from './LoginPage';

const tenants = [
  { id: 't-other', name: 'Another Ltd', businessTimeZone: 'Africa/Lagos' },
  { id: 't-demo', name: 'Demo Trading Ltd', businessTimeZone: 'Africa/Lagos' },
];

describe('LoginPage', () => {
  beforeEach(() => sessionStorage.clear());

  it('on the demo, explains the sample data and preselects the demo tenant', async () => {
    mockApi({ 'GET /dev/environment': () => ({ name: 'Demo' }), 'GET /dev/tenants': () => tenants });

    renderAt(<LoginPage />, { url: '/login', path: '/login' });

    expect(await screen.findByText(/February 2026's sample data/)).toBeInTheDocument();
    expect(await screen.findByRole('option', { name: 'Demo Trading Ltd', selected: true })).toBeInTheDocument();
  });

  it('in development, does not preselect a tenant', async () => {
    mockApi({ 'GET /dev/environment': () => ({ name: 'Development' }), 'GET /dev/tenants': () => tenants });

    renderAt(<LoginPage />, { url: '/login', path: '/login' });

    await screen.findByRole('option', { name: 'Demo Trading Ltd' });
    expect(screen.getByRole('option', { name: 'New tenant…', selected: true })).toBeInTheDocument();
    expect(screen.getByText(/Development sign-in/)).toBeInTheDocument();
  });

  it('shows why the tenant list is missing and lets the user retry', async () => {
    let fail = true;
    mockApi({
      'GET /dev/environment': () => ({ name: 'Demo' }),
      'GET /dev/tenants': (): unknown => {
        if (fail) {
          throw new Error('unreachable');
        }
        return tenants;
      },
    });
    globalThis.fetch = wrapFailure(globalThis.fetch, () => fail);

    renderAt(<LoginPage />, { url: '/login', path: '/login' });

    expect(await screen.findByRole('alert')).toHaveTextContent('Service unavailable');
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry loading tenants' }));
    expect(await screen.findByRole('option', { name: 'Demo Trading Ltd' })).toBeInTheDocument();
  });
});

/** Makes GET /dev/tenants answer 503 while `failing()` is true. */
function wrapFailure(inner: typeof fetch, failing: () => boolean): typeof fetch {
  return (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : String(input);
    if (failing() && url.endsWith('/dev/tenants')) {
      return new Response(JSON.stringify({ title: 'Unavailable', detail: 'Service unavailable' }), { status: 503 });
    }
    return inner(input, init);
  }) as typeof fetch;
}
