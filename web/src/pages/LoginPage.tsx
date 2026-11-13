import { useMutation, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, unwrap } from '../api/client';
import type { Role } from '../api/types';
import { useSession } from '../auth/session';
import { ErrorBanner, Notice } from '../components/ui';

const allRoles: Role[] = ['Admin', 'Preparer', 'Approver', 'Viewer'];

/**
 * Development sign-in: pick a tenant, a user name and roles, and get a token from /dev/token (ADR 0003).
 * A real identity provider replaces only this page.
 */
export function LoginPage() {
  const { signIn } = useSession();
  const navigate = useNavigate();
  const tenants = useQuery({ queryKey: ['dev-tenants'], queryFn: () => unwrap(api.GET('/dev/tenants')) });
  const [tenantId, setTenantId] = useState('');
  const [newTenant, setNewTenant] = useState('');
  const [userId, setUserId] = useState('');
  const [roles, setRoles] = useState<Role[]>(['Preparer']);

  const signInMutation = useMutation({
    mutationFn: async () => {
      let id = tenantId;
      let name = tenants.data?.find((t) => t.id === id)?.name ?? '';
      if (!id) {
        id = (await unwrap(api.POST('/dev/tenants', { body: { name: newTenant, businessTimeZone: null } }))).tenantId;
        name = newTenant;
      }
      const token = await unwrap(api.POST('/dev/token', { body: { tenantId: id, userId, roles } }));
      return { token: token.accessToken, expiresAt: token.expiresAt, tenantId: id, tenantName: name, userId, roles };
    },
    onSuccess: (session) => {
      signIn(session);
      navigate('/');
    },
  });

  return (
    <section className="card narrow">
      <h1>Sign in</h1>
      <Notice>Development sign-in. Tokens come from the API's /dev/token endpoint.</Notice>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          signInMutation.mutate();
        }}
      >
        <label htmlFor="tenant">Tenant</label>
        <select id="tenant" value={tenantId} onChange={(e) => setTenantId(e.target.value)}>
          <option value="">New tenant…</option>
          {tenants.data?.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </select>
        {!tenantId && (
          <>
            <label htmlFor="new-tenant">New tenant name</label>
            <input id="new-tenant" value={newTenant} onChange={(e) => setNewTenant(e.target.value)} required />
          </>
        )}
        <label htmlFor="user">User name</label>
        <input id="user" value={userId} onChange={(e) => setUserId(e.target.value)} required autoComplete="username" />
        <fieldset>
          <legend>Roles</legend>
          {allRoles.map((role) => (
            <label key={role} className="check">
              <input
                type="checkbox"
                checked={roles.includes(role)}
                onChange={(e) => setRoles((r) => (e.target.checked ? [...r, role] : r.filter((x) => x !== role)))}
              />
              {role}
            </label>
          ))}
        </fieldset>
        <ErrorBanner error={signInMutation.error} />
        <button type="submit" disabled={signInMutation.isPending || roles.length === 0}>
          Sign in
        </button>
      </form>
    </section>
  );
}
