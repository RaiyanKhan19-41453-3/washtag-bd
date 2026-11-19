import { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { LanguageProvider } from './context/LanguageContext';
import { ToastProvider } from './context/ToastContext';
import { AuthProvider } from './context/AuthContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import Loader from './components/Loader';

const Landing = lazy(() => import('./pages/Landing'));
const Login = lazy(() => import('./pages/Login'));
const Register = lazy(() => import('./pages/Register'));
const Dashboard = lazy(() => import('./pages/Dashboard'));
const Orders = lazy(() => import('./pages/Orders'));
const NewOrder = lazy(() => import('./pages/NewOrder'));
const OrderDetail = lazy(() => import('./pages/OrderDetail'));
const Scan = lazy(() => import('./pages/Scan'));
const Customers = lazy(() => import('./pages/Customers'));
const Team = lazy(() => import('./pages/Team'));
const Billing = lazy(() => import('./pages/Billing'));
const Settings = lazy(() => import('./pages/Settings'));
const Assistant = lazy(() => import('./pages/Assistant'));
const Admin = lazy(() => import('./pages/Admin'));
const PrintReceipt = lazy(() => import('./pages/PrintReceipt'));
const Track = lazy(() => import('./pages/Track'));
const NotFound = lazy(() => import('./pages/NotFound'));

export default function App() {
  return (
    <BrowserRouter>
      <LanguageProvider>
        <ToastProvider>
          <AuthProvider>
            <Suspense fallback={<Loader full label="Loading…" />}>
              <Routes>
                <Route path="/" element={<Landing />} />
                <Route path="/login" element={<Login />} />
                <Route path="/register" element={<Register />} />
                <Route path="/track/:token" element={<Track />} />
                <Route path="/t/:token" element={<Track />} />
                <Route path="/print/:token" element={<PrintReceipt />} />

                <Route element={<ProtectedRoute />}>
                  <Route path="/app" element={<Layout />}>
                    <Route index element={<Dashboard />} />
                    <Route path="orders" element={<Orders />} />
                    <Route path="orders/new" element={<NewOrder />} />
                    <Route path="orders/:id" element={<OrderDetail />} />
                    <Route path="scan" element={<Scan />} />
                    <Route path="customers" element={<Customers />} />
                    <Route path="team" element={<Team />} />
                    <Route path="billing" element={<Billing />} />
                    <Route path="admin" element={<Admin />} />
                    <Route path="assistant" element={<Assistant />} />
                    <Route path="settings" element={<Settings />} />
                  </Route>
                </Route>

                <Route path="*" element={<NotFound />} />
              </Routes>
            </Suspense>
          </AuthProvider>
        </ToastProvider>
      </LanguageProvider>
    </BrowserRouter>
  );
}
