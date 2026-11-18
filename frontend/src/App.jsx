import { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './context/ToastContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import Landing from './pages/Landing';
import Login from './pages/Login';
import Register from './pages/Register';
import Dashboard from './pages/Dashboard';
import Orders from './pages/Orders';
import NewOrder from './pages/NewOrder';
import OrderDetail from './pages/OrderDetail';
import Loader from './components/Loader';
import Customers from './pages/Customers';
import Settings from './pages/Settings';
import Assistant from './pages/Assistant';
import PrintReceipt from './pages/PrintReceipt';
import Track from './pages/Track';
import NotFound from './pages/NotFound';

const Scan = lazy(() => import('./pages/Scan'));

export default function App() {
  return (
    <BrowserRouter>
      <ToastProvider>
        <AuthProvider>
          <Suspense fallback={<Loader full label="Loading page…" />}>
          <Routes>
            <Route path="/" element={<Landing />} />
            <Route path="/login" element={<Login />} />
            <Route path="/register" element={<Register />} />
            <Route path="/track/:token" element={<Track />} />
            <Route path="/print/:token" element={<PrintReceipt />} />
            <Route path="/t/:token" element={<Track />} />

            <Route element={<ProtectedRoute />}>
              <Route path="/app" element={<Layout />}>
                <Route index element={<Dashboard />} />
                <Route path="assistant" element={<Assistant />} />
                <Route path="orders" element={<Orders />} />
                <Route path="orders/new" element={<NewOrder />} />
                <Route path="orders/:id" element={<OrderDetail />} />
                <Route path="scan" element={<Scan />} />
                <Route path="customers" element={<Customers />} />
                <Route path="settings" element={<Settings />} />
              </Route>
            </Route>

            <Route path="*" element={<NotFound />} />
          </Routes>
          </Suspense>
        </AuthProvider>
      </ToastProvider>
    </BrowserRouter>
  );
}
