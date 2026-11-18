import { useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import {
  LayoutDashboard, Package, ScanLine, Users, Settings as SettingsIcon,
  LogOut, Plus, Menu, Shirt, Sparkles
} from 'lucide-react';
import { useAuth } from '../context/AuthContext';
import { greeting, initials } from '../utils/helpers';

const navItems = [
  { to: '/app', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/app/assistant', label: 'AI Assistant', icon: Sparkles },
  { to: '/app/orders', label: 'Orders', icon: Package },
  { to: '/app/scan', label: 'Scan QR', icon: ScanLine },
  { to: '/app/customers', label: 'Customers', icon: Users },
  { to: '/app/settings', label: 'Settings', icon: SettingsIcon }
];

function titleFor(pathname) {
  if (pathname === '/app') return 'Dashboard';
  if (pathname.startsWith('/app/assistant')) return 'AI Assistant';
  if (pathname.startsWith('/app/orders/new')) return 'New Order';
  if (pathname.startsWith('/app/orders/')) return 'Order Details';
  if (pathname.startsWith('/app/orders')) return 'Orders';
  if (pathname.startsWith('/app/scan')) return 'Scan QR';
  if (pathname.startsWith('/app/customers')) return 'Customers';
  if (pathname.startsWith('/app/settings')) return 'Settings';
  return 'Workspace';
}

export default function Layout() {
  const [open, setOpen] = useState(false);
  const { user, shop, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();

  const handleLogout = () => {
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="app-shell">
      <aside className={`sidebar ${open ? 'is-open' : ''}`}>
        <div className="brand">
          <div className="brand-mark"><Shirt size={21} /></div>
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
          <span className="nav-caption">WORKSPACE</span>
          {navItems.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}
              onClick={() => setOpen(false)}
            >
              <Icon size={19} />
              <span>{label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="sidebar-foot">
          <div className="upgrade-card">
            <Sparkles size={17} />
            <strong>{shop?.plan || 'Starter'} Plan</strong>
            <p>Active until {shop?.subscriptionEndsAt ? new Date(shop.subscriptionEndsAt).toLocaleDateString('en-GB') : '—'}</p>
          </div>
          <button className="nav-link logout-link" onClick={handleLogout}>
            <LogOut size={19} />
            <span>Log out</span>
          </button>
        </div>
      </aside>

      {open && <div className="sidebar-overlay" onClick={() => setOpen(false)} />}

      <div className="app-main">
        <header className="topbar">
          <button className="icon-btn menu-btn" onClick={() => setOpen((value) => !value)} aria-label="Toggle menu">
            <Menu size={21} />
          </button>
          <div className="topbar-title">
            <h1>{titleFor(location.pathname)}</h1>
            <p>{greeting()}, {user?.name?.split(' ')[0]} 👋</p>
          </div>
          <div className="topbar-actions">
            <Link to="/app/orders/new" className="btn btn-primary btn-sm">
              <Plus size={16} /> <span>New Order</span>
            </Link>
            <div className="user-chip">
              <span className="avatar">{initials(user?.name)}</span>
              <div>
                <strong>{user?.name}</strong>
                <small>{user?.role}</small>
              </div>
            </div>
            <button className="icon-btn logout-btn" onClick={handleLogout} title="Log out">
              <LogOut size={18} />
            </button>
          </div>
        </header>

        <main className="page-content"><Outlet /></main>
      </div>
    </div>
  );
}
