import { useQuery } from '@tanstack/react-query';
import { NavLink, Outlet, useParams } from 'react-router-dom';
import { api, unwrap } from '../api/client';
import { useMonth } from '../components/period';
import { ErrorBanner } from '../components/ui';

const tabs = [
  ['', 'Dashboard'],
  ['upload', 'Upload'],
  ['review', 'Review'],
  ['exceptions', 'Exceptions'],
  ['matched', 'Matched'],
] as const;

export function AccountPage() {
  const { accountId = '' } = useParams();
  const { month, setMonth } = useMonth();
  const account = useQuery({
    queryKey: ['account', accountId],
    queryFn: () => unwrap(api.GET('/api/core/accounts/{id}', { params: { path: { id: accountId } } })),
  });

  return (
    <section>
      <div className="page-head">
        <h1>{account.data?.displayName ?? 'Account'}</h1>
        <label className="month">
          Month <input type="month" value={month} onChange={(e) => e.target.value && setMonth(e.target.value)} />
        </label>
      </div>
      <ErrorBanner error={account.error} />
      <nav className="tabs" aria-label="Account sections">
        {tabs.map(([path, label]) => (
          <NavLink key={label} to={`/accounts/${accountId}${path ? `/${path}` : ''}?month=${month}`} end>
            {label}
          </NavLink>
        ))}
      </nav>
      <Outlet context={account.data} />
    </section>
  );
}
