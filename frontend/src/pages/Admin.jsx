import { useCallback, useEffect, useState } from 'react';
import { Store, Users, Package, Wallet, ShieldAlert, Ban, CheckCircle2 } from 'lucide-react';
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
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    api.get('/admin/stats').then(({ data }) => { setStats(data); setError(''); })
      .catch((err) => setError(err.message));
  }, []);

  useEffect(() => {
    if (user?.isAdmin) load();
  }, [user?.isAdmin, load]);

  const mutate = async (path, method = 'PATCH', body) => {
    setBusy(true);
    try {
      await api.request({ url: `/admin/shops/${path}`, method, data: body });
      toast.success('Shop updated.');
      load();
    } catch (err) { toast.error(err.message); }
    finally { setBusy(false); }
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
                        <button className="btn btn-ghost btn-sm" disabled={busy} onClick={() => mutate(`${shop.shopId}/active`, 'PATCH', { active: false })} title="Suspend shop"><Ban size={15} /> Suspend</button>
                      ) : (
                        <button className="btn btn-ghost btn-sm" disabled={busy} onClick={() => mutate(`${shop.shopId}/active`, 'PATCH', { active: true })} title="Reactivate shop"><CheckCircle2 size={15} /> Activate</button>
                      )}
                      {shop.subscriptionStatus !== 'Cancelled' && (
                        <button className="btn btn-ghost btn-sm danger-text" disabled={busy} onClick={() => mutate(`${shop.shopId}/cancel`)}>Cancel plan</button>
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
