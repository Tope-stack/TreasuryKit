import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useSession } from '../auth/session';
import { useEnvironment } from './environment';

export function Layout() {
  const { session, signOut } = useSession();
  const navigate = useNavigate();
  // Present only where the dev sign-in is enabled; a demo deployment says so on every page (ADR 0010).
  const environment = useEnvironment();
  const demo = environment.data && environment.data !== 'Development';
  return (
    <div className="shell">
      {demo && (
        <div className="demo-banner" role="note">
          {environment.data} environment — synthetic data only. Anyone signed in here can act as any user.
        </div>
      )}
      <header className="topbar">
        <NavLink to="/" className="brand">
          TreasuryKit
        </NavLink>
        {session && (
          <div className="whoami">
            <span>
              {session.userId} · {session.roles.join(', ')} · {session.tenantName}
            </span>
            <button
              type="button"
              className="link"
              onClick={() => {
                signOut();
                navigate('/login');
              }}
            >
              Sign out
            </button>
          </div>
        )}
      </header>
      <main>
        <Outlet />
      </main>
    </div>
  );
}
