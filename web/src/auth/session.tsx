import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import type { Role } from '../api/types';

export type Session = { token: string; tenantId: string; tenantName: string; userId: string; roles: Role[]; expiresAt: string };

const key = 'treasurykit.session';

export function loadSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(key);
    if (!raw) return null;
    const session = JSON.parse(raw) as Session;
    return new Date(session.expiresAt) > new Date() ? session : null;
  } catch {
    return null;
  }
}

type SessionContext = {
  session: Session | null;
  signIn: (session: Session) => void;
  signOut: () => void;
  /** UI gating only; the API enforces every rule itself. */
  can: (...roles: Role[]) => boolean;
};

const Context = createContext<SessionContext | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(() => loadSession());

  const signIn = useCallback((value: Session) => {
    sessionStorage.setItem(key, JSON.stringify(value));
    setSession(value);
  }, []);

  const signOut = useCallback(() => {
    sessionStorage.removeItem(key);
    setSession(null);
  }, []);

  const value = useMemo<SessionContext>(
    () => ({
      session,
      signIn,
      signOut,
      can: (...roles) => !!session && (session.roles.includes('Admin') || roles.some((r) => session.roles.includes(r))),
    }),
    [session, signIn, signOut],
  );

  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function useSession(): SessionContext {
  const context = useContext(Context);
  if (!context) throw new Error('useSession must be used inside SessionProvider');
  return context;
}
