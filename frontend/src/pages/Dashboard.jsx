import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Package, CheckCircle2, Wallet, AlertCircle, ArrowUpRight, RefreshCw,
  ScanLine, Plus, TrendingUp
} from 'lucide-react';
import api from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import { useRealtime } from '../hooks/useRealtime';
import StatCard from '../components/StatCard';
import OrderTable from '../components/OrderTable';
import Loader from '../components/Loader';
import { money, STATUS_LABELS, greeting } from '../utils/helpers';

const COLORS = {
  Received: '#64748b',
  Washing: '#0ea5e9',
  Ironing: '#f59e0b',
  ReadyForPickup: '#8b5cf6',
  Delivered: '#10b981',
  Cancelled: '#ef4444'
};

export default function Dashboard() {
  const [data, setData] = useState(null);
  const [error, setError] = useState('');
  const { user } = useAuth();
  const { t } = useLanguage();
  const token = localStorage.getItem('washtag_token');

  const load = useCallback(() => {
    api.get('/dashboard')
      .then(({ data: result }) => { setData(result); setError(''); })
      .catch((err) => setError(err.message));
  }, []);

  useEffect(() => {
    load();
    const timer = window.setInterval(load, 60000);
    return () => window.clearInterval(timer);
  }, [load]);

  useRealtime({ token, onUpdate: load });

  if (!data && !error) return <Loader full label="Loading your dashboard…" />;

  if (error && !data) {
    return (
      <div className="card error-card">
        <AlertCircle size={22} />
        <p>{error}</p>
        <button className="btn btn-outline btn-sm" onClick={load}><RefreshCw size={15} /> Retry</button>
      </div>
    );
  }

  const maxCount = Math.max(1, ...data.weeklyTrend.map((day) => day.count));
  const total = Math.max(1, data.totalOrders);
  let cursor = 0;
  const gradientStops = data.statusBreakdown.map((item) => {
    const start = (cursor / total) * 100;
    cursor += item.count;
    const end = (cursor / total) * 100;
    return `${COLORS[item.status] || '#cbd5e1'} ${start}% ${end}%`;
  });
  const donut = data.totalOrders > 0
    ? `conic-gradient(${gradientStops.join(', ')})`
    : 'conic-gradient(#e2e8f0 0% 100%)';

  return (
    <div className="page-stack">
      <div className="welcome-row">
        <div>
          <h2>{t(greeting())}, {user?.name?.split(' ')[0]} 👋</h2>
          <p>{t("Here's what's happening at your counter today.")}</p>
        </div>
        <div className="welcome-actions">
          <Link to="/app/scan" className="btn btn-outline btn-sm"><ScanLine size={16} /> {t('Scan QR')}</Link>
          <Link to="/app/orders/new" className="btn btn-primary btn-sm"><Plus size={16} /> {t('New Order')}</Link>
        </div>
      </div>

      <div className="stat-grid">
        <StatCard icon={Package} label={t('Active orders')} value={data.activeCount} hint={`${data.totalOrders} ${t('all time')}`} tone="teal" />
        <StatCard icon={CheckCircle2} label={t('Ready for pickup')} value={data.readyCount} hint={t('Waiting for customers')} tone="violet" />
        <StatCard icon={Wallet} label={t('Collected')} value={money(data.collected)} hint={`${money(data.outstanding)} ${t('outstanding')}`} tone="emerald" />
        <StatCard icon={AlertCircle} label={t('Unpaid orders')} value={data.unpaidCount} hint={`${data.customerCount} ${t('customers')}`} tone="amber" />
      </div>

      <div className="dashboard-grid">
        <section className="card chart-card">
          <div className="card-head">
            <div>
              <h3>{t('Orders this week')}</h3>
              <p>{t('Last 7 days at a glance')}</p>
            </div>
            <span className="card-chip"><TrendingUp size={14} /> {data.todayOrders} {t('today')}</span>
          </div>
          <div className="bar-chart">
            {data.weeklyTrend.map((day) => (
              <div key={day.label} className="bar-col">
                <strong>{day.count}</strong>
                <div className="bar-track">
                  <div className="bar-fill" style={{ height: `${Math.max(5, (day.count / maxCount) * 100)}%` }} />
                </div>
                <span>{day.label}</span>
              </div>
            ))}
          </div>
        </section>

        <section className="card breakdown-card">
          <div className="card-head">
            <div>
              <h3>{t('Order mix')}</h3>
              <p>{t('Where your orders stand')}</p>
            </div>
          </div>
          <div className="breakdown-body">
            <div className="donut" style={{ background: donut }}>
              <div className="donut-hole">
                <strong>{data.totalOrders}</strong>
                <span>{t('orders')}</span>
              </div>
            </div>
            <div className="legend">
              {data.statusBreakdown.filter((item) => item.count > 0).map((item) => (
                <div key={item.status} className="legend-row">
                  <i style={{ background: COLORS[item.status] }} />
                  <span>{t(STATUS_LABELS[item.status])}</span>
                  <strong>{item.count}</strong>
                </div>
              ))}
              {data.totalOrders === 0 && <p className="muted">No orders yet.</p>}
            </div>
          </div>
        </section>
      </div>

      <section className="card">
        <div className="card-head">
          <div>
            <h3>{t('Recent orders')}</h3>
            <p>{t('The latest movements at your counter')}</p>
          </div>
          <Link to="/app/orders" className="text-link">{t('View all')} <ArrowUpRight size={15} /></Link>
        </div>
        <OrderTable orders={data.recentOrders} />
      </section>
    </div>
  );
}
