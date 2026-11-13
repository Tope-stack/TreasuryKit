import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api, unwrap } from '../api/client';
import { useSession } from '../auth/session';
import { Badge, Empty, ErrorBanner } from '../components/ui';

export function AccountsPage() {
  const { can } = useSession();
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: () => unwrap(api.GET('/api/core/accounts')) });

  return (
    <section>
      <h1>Bank accounts</h1>
      <ErrorBanner error={accounts.error} />
      {accounts.data?.length === 0 && <Empty>No accounts yet.</Empty>}
      <ul className="account-list">
        {accounts.data?.map((a) => (
          <li key={a.id}>
            <Link to={`/accounts/${a.id}`}>{a.displayName}</Link> <Badge>{a.currency}</Badge>
            <span className="muted">
              {' '}
              {a.bankCode} · {a.accountNumber}
            </span>
          </li>
        ))}
      </ul>
      {can('Admin') && <CreateAccount />}
    </section>
  );
}

function CreateAccount() {
  const client = useQueryClient();
  const profiles = useQuery({ queryKey: ['profiles'], queryFn: () => unwrap(api.GET('/api/core/mapping-profiles')) });
  const [form, setForm] = useState({ bankCode: '', bankName: '', accountNumber: '', currency: 'NGN', mappingProfileId: '' });
  const create = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/core/accounts', {
          body: { ...form, displayName: null, mappingProfileId: form.mappingProfileId || null },
        }),
      ),
    onSuccess: () => {
      setForm({ bankCode: '', bankName: '', accountNumber: '', currency: 'NGN', mappingProfileId: '' });
      return client.invalidateQueries({ queryKey: ['accounts'] });
    },
  });
  const field = (name: keyof typeof form, label: string) => (
    <>
      <label htmlFor={name}>{label}</label>
      <input id={name} value={form[name]} onChange={(e) => setForm({ ...form, [name]: e.target.value })} required />
    </>
  );

  return (
    <form
      className="card"
      onSubmit={(e) => {
        e.preventDefault();
        create.mutate();
      }}
    >
      <h2>New account</h2>
      <div className="grid-2">
        {field('bankCode', 'Bank code')}
        {field('bankName', 'Bank name')}
        {field('accountNumber', 'Account number')}
        {field('currency', 'Currency')}
        <label htmlFor="profile">Mapping profile (CSV/XLSX)</label>
        <select id="profile" value={form.mappingProfileId} onChange={(e) => setForm({ ...form, mappingProfileId: e.target.value })}>
          <option value="">None (MT940 / CAMT.053 only)</option>
          {profiles.data?.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
      </div>
      <ErrorBanner error={create.error} />
      <button type="submit" disabled={create.isPending}>
        Create account
      </button>
    </form>
  );
}
