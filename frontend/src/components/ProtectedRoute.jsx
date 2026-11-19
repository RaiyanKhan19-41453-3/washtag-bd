import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import Loader from './Loader';

export default function ProtectedRoute() {
  const { isAuthenticated, loading, profileError, retryProfile, logout } = useAuth();
  const location = useLocation();

  if (loading) return <Loader full label="Opening your workspace…" />;
  if (profileError && !isAuthenticated) {
    return (
      <div className="login-shell">
        <div className="card error-card">
          <p>{profileError}</p>
          <div className="form-actions">
            <button className="btn btn-primary" onClick={retryProfile}>Retry</button>
            <button className="btn btn-outline" onClick={logout}>Sign out</button>
          </div>
        </div>
      </div>
    );
  }
  if (!isAuthenticated) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  return <Outlet />;
}
