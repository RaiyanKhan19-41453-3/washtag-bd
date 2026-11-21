import { useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import {
  LayoutDashboard, Package, ScanLine, Users, Settings as SettingsIcon,
  LogOut, Plus, Menu, Sparkles, UserCog, CreditCard, Shield, AlertCircle, BarChart3
} from 'lucide-react';
import LogoMark from './LogoMark';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import { useToast } from '../context/ToastContext';
import { useRealtime } from '../hooks/useRealtime';
import LanguageToggle from './LanguageToggle';
import { greeting, initials, shopLocked } from '../utils/helpers';

const navItems = [
  { to: '/app', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/app/assistant', label: 'AI Assistant', icon: Sparkles },
  { to: '/app/orders', label: 'Orders', icon: Package },
  { to: '/app/scan', label: 'Scan QR', icon: ScanLine },
  { to: '/app/customers', label: 'Customers', icon: Users },
  { to: '/app/team', label: 'Team', icon: UserCog },
  { to: '/app/billing', label: 'Billing', icon: CreditCard },
  { to: '/app/reports', label: 'Reports', icon: BarChart3 },
  { to: '/app/settings', label: 'Settings', icon: SettingsIcon }
];

function titleFor(pathname) {
  if (pathname === '/app') return 'Dashboard';
  if (pathname.startsWith('/app/assistant')) return 'AI Assistant';
  if (pathname.startsWith('/app/orders/new')) return 'New Order';
  if (pathname.startsWith('/app/orders/')) return 'Order details';
  if (pathname.startsWith('/app/orders')) return 'Orders';
  if (pathname.startsWith('/app/scan')) return 'Scan QR';
  if (pathname.startsWith('/app/customers')) return 'Customers';
  if (pathname.startsWith('/app/team')) return 'Team';
  if (pathname.startsWith('/app/billing')) return 'Billing';
  if (pathname.startsWith('/app/reports')) return 'Reports';
  if (pathname.startsWith('/app/admin')) return 'Platform Admin';
  if (pathname.startsWith('/app/settings')) return 'Settings';
  return 'WORKSPACE';
}

export default function Layout() {
  const [open, setOpen] = useState(false);
  const { user, shop, logout } = useAuth();
  const { t } = useLanguage();
  const location = useLocation();
  const navigate = useNavigate();
  const toast = useToast();
  const token = localStorage.getItem('washtag_token');

  // The shell is always mounted, so order events are visible even when the owner
  // is on a page that does not subscribe to realtime itself.
  useRealtime({
    token,
    onUpdate: () => {
      if (!location.pathname.startsWith('/app/orders') && !location.pathname.startsWith('/app/scan'))
        toast.info('An order was updated. Open Orders to see the latest.');
    }
  });

  const handleLogout = () => {
    logout();
    navigate('/login', { replace: true });
  };

  const navigation = user?.isAdmin
    ? [...navItems, { to: '/app/admin', label: 'Platform Admin', icon: Shield }]
    : navItems;

  return (
    <div className="app-shell">
      <aside className={`sidebar ${open ? 'is-open' : ''}`}>
        <div className="brand">
          <div className="brand-mark"><LogoMark /></div>
          <div className="brand-text">
            <strong>WashTag<span>BD</span></strong>
            <small>Smart Laundry OS</small>
          </div>
        </div>

        <div className="shop-chip">
          <span className="shop-avatar">{initials(shop?.name)}</span>
          <div>
            <strong>{shop?.name || 'Your Shop'}</strong>
            <small>{shop?.city || 'Bangladesh'}</small>
          </div>
        </div>

        <nav className="side-nav">
          <span className="nav-caption">{t('WORKSPACE')}</span>
          {navigation.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}
              onClick={() => setOpen(false)}
            >
              <Icon size={19} />
              <span>{t(label)}</span>
            </NavLink>
          ))}
        </nav>

        <div className="sidebar-foot">
          <div className="upgrade-card">
            <Sparkles size={17} />
            <strong>{shop?.plan || 'Starter'} Plan</strong>
            <p>Active until {shop?.subscriptionEndsAt ? new Date(shop.subscriptionEndsAt).toLocaleDateString('en-GB') : '-'}</p>
          </div>
          <button className="nav-link logout-link" onClick={handleLogout}>
            <LogOut size={19} />
            <span>{t('Log out')}</span>
          </button>
        </div>
      </aside>

      {open && <div className="sidebar-overlay" onClick={() => setOpen(false)} />}

      <div className="app-main">
        {shopLocked(shop) && (
          <div className="subscription-banner">
            <AlertCircle size={17} /> <span>{t('Your subscription is inactive. Renew to keep managing orders.')}</span>
            <Link to="/app/billing" className="btn btn-sm">{t('Renew now')}</Link>
          </div>
        )}
        <header className="topbar">
          <button className="icon-btn menu-btn" onClick={() => setOpen((value) => !value)} aria-label="Toggle menu">
            <Menu size={21} />
          </button>
          <div className="topbar-title">
            <h1>{t(titleFor(location.pathname))}</h1>
            <p>{t(greeting())}, {user?.name?.split(' ')[0]} 👋</p>
          </div>
          <div className="topbar-actions">
            <LanguageToggle />
            <Link to="/app/orders/new" className="btn btn-primary btn-sm">
              <Plus size={16} /> <span>{t('New Order')}</span>
            </Link>
            <div className="user-chip">
              <span className="avatar">{initials(user?.name)}</span>
              <div>
                <strong>{user?.name}</strong>
                <small>{user?.role}</small>
              </div>
            </div>
            <button className="icon-btn logout-btn" onClick={handleLogout} title={t('Log out')}>
              <LogOut size={18} />
            </button>
          </div>
        </header>

        <main className="page-content"><Outlet /></main>
      </div>
    </div>
  );
}
