import { useCallback, useEffect, useState } from 'react';
import { Store, Users, Package, Wallet, ShieldAlert, Ban, CheckCircle2, RotateCcw } from 'lucide-react';
import api from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import StatCard from '../components/StatCard';
import Loader from '../components/Loader';
import { money, formatDate } from '../utils/helpers';

export default function Admin() {
  const { user } = useAuth();
  const toast = useToast();
  const [stats, setStats] = useState(null);
  const [error, setError] = useState('');
  const [busyId, setBusyId] = useState(null);

  const load = useCallback(() => {
    api.get('/admin/stats').then(({ data }) => { setStats(data); setError(''); })
      .catch((err) => setError(err.message));
  }, []);

  useEffect(() => {
    if (user?.isAdmin) load();
  }, [user?.isAdmin, load]);

  const mutate = async (shop, path, message, method = 'PATCH', body) => {
    setBusyId(shop.shopId);
    try {
      await api.request({ url: `/admin/shops/${path}`, method, data: body });
      toast.success(message);
      load();
    } catch (err) { toast.error(err.message); }
    finally { setBusyId(null); }
  };

  const confirmCancel = (shop) => {
    if (window.confirm(`Cancel the subscription for ${shop.shopName}? This locks the shop until an admin restores its plan.`)) {
      mutate(shop, `${shop.shopId}/cancel`, 'Subscription cancelled.');
    }
  };

  if (!user?.isAdmin) {
    return <div className="card error-card"><ShieldAlert size={22} /><p>Platform admin access is required for this page.</p></div>;
  }
  if (!stats && !error) return <Loader full label="Loading platform overview…" />;
  if (error) return <div className="card error-card"><p>{error}</p><button className="btn btn-outline btn-sm" onClick={load}>Retry</button></div>;

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div><h2>Platform overview</h2><p>All shops on your WashTag BD deployment.</p></div>
      </div>

      <div className="stat-grid">
        <StatCard icon={Store} label="Shops" value={stats.shopCount} hint={`${stats.activeShopCount} active`} tone="teal" />
        <StatCard icon={Users} label="Accounts" value={stats.userCount} hint="Owners + staff" tone="violet" />
        <StatCard icon={Package} label="Orders" value={stats.orderCount} hint="Across all shops" tone="amber" />
        <StatCard icon={Wallet} label="Collected" value={money(stats.collected)} hint="All-time payments" tone="emerald" />
      </div>

      <section className="card">
        <div className="card-head"><div><h3>Shops</h3><p>Subscription and activity by tenant</p></div></div>
        <div className="table-scroll">
          <table className="data-table">
            <thead><tr><th>Shop</th><th>Owner</th><th>Plan</th><th>Subscription</th><th className="align-right">Orders</th><th className="align-right">Collected</th><th>Renews</th><th /></tr></thead>
            <tbody>
              {stats.shops.map((shop) => (
                <tr key={shop.shopId}>
                  <td><strong>{shop.shopName}</strong><div className="cell-secondary">{shop.city}</div></td>
                  <td><div className="cell-primary">{shop.ownerName}</div><div className="cell-secondary">{shop.email}</div></td>
                  <td><span className="soft-chip">{shop.plan}</span></td>
                  <td><span className={`badge payment-${shop.subscriptionStatus === 'Active' ? 'Paid' : 'Partial'}`}>{shop.subscriptionStatus}</span></td>
                  <td className="align-right">{shop.orderCount}</td>
                  <td className="align-right strong">{money(shop.collected)}</td>
                  <td className="muted">{formatDate(shop.renewalDate)}</td>
                  <td>
                    <div className="team-actions">
                      {shop.isActive ? (
                        <button className="btn btn-ghost btn-sm" disabled={busyId === shop.shopId}
                          onClick={() => { if (window.confirm(`Suspend ${shop.shopName}? Its owners will lose access until reactivated.`)) mutate(shop, `${shop.shopId}/active`, 'Shop suspended.', 'PATCH', { active: false }); }}>
                          <Ban size={15} /> Suspend
                        </button>
                      ) : (
                        <button className="btn btn-ghost btn-sm" disabled={busyId === shop.shopId}
                          onClick={() => mutate(shop, `${shop.shopId}/active`, 'Shop reactivated.', 'PATCH', { active: true })}>
                          <CheckCircle2 size={15} /> Activate
                        </button>
                      )}
                      {shop.subscriptionStatus === 'Cancelled' || shop.subscriptionStatus === 'PastDue' ? (
                        <button className="btn btn-ghost btn-sm" disabled={busyId === shop.shopId}
                          onClick={() => { if (window.confirm(`Restore the plan for ${shop.shopName}?`)) mutate(shop, `${shop.shopId}/restore`, 'Plan restored.'); }}>
                          <RotateCcw size={15} /> Restore
                        </button>
                      ) : (
                        <button className="btn btn-ghost btn-sm danger-text" disabled={busyId === shop.shopId}
                          onClick={() => confirmCancel(shop)}>Cancel plan</button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
