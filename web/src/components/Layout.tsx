import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useSession } from '../auth/session';

export function Layout() {
  const { session, signOut } = useSession();
  const navigate = useNavigate();
  return (
    <div className="shell">
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
