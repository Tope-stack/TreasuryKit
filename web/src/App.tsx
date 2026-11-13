import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useSession } from './auth/session';
import { Layout } from './components/Layout';
import { AccountPage } from './pages/AccountPage';
import { AccountsPage } from './pages/AccountsPage';
import { DashboardPage } from './pages/DashboardPage';
import { ExceptionsPage } from './pages/ExceptionsPage';
import { LoginPage } from './pages/LoginPage';
import { MatchedPage } from './pages/MatchedPage';
import { ReviewPage } from './pages/ReviewPage';
import { UploadPage } from './pages/UploadPage';

function RequireSession({ children }: { children: ReactNode }) {
  const { session } = useSession();
  const location = useLocation();
  return session ? children : <Navigate to="/login" replace state={{ from: location }} />;
}

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="login" element={<LoginPage />} />
        <Route
          index
          element={
            <RequireSession>
              <AccountsPage />
            </RequireSession>
          }
        />
        <Route
          path="accounts/:accountId"
          element={
            <RequireSession>
              <AccountPage />
            </RequireSession>
          }
        >
          <Route index element={<DashboardPage />} />
          <Route path="upload" element={<UploadPage />} />
          <Route path="review" element={<ReviewPage />} />
          <Route path="exceptions" element={<ExceptionsPage />} />
          <Route path="matched" element={<MatchedPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
