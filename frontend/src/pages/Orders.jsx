import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Plus, Search, PackageOpen, ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react';
import api from '../api/client';
import { useRealtime } from '../hooks/useRealtime';
import { useLanguage } from '../context/LanguageContext';
import OrderTable from '../components/OrderTable';
import EmptyState from '../components/EmptyState';
import Loader from '../components/Loader';
import { STATUS_LABELS } from '../utils/helpers';

const FILTERS = ['', 'Received', 'Washing', 'Ironing', 'ReadyForPickup', 'Delivered', 'Cancelled'];
const PAGE_SIZE = 12;

export default function Orders() {
  const [orders, setOrders] = useState([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const token = localStorage.getItem('washtag_token');
  const { t } = useLanguage();

  const load = useCallback(() => {
    setLoading(true);
    api.get('/orders', { params: { status, search, page, pageSize: PAGE_SIZE } })
      .then(({ data }) => { setOrders(data.items); setTotal(data.total); setError(''); })
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false));
  }, [status, search, page]);

  useEffect(() => {
    const timer = window.setTimeout(load, 200);
    return () => window.clearTimeout(timer);
  }, [load]);

  useRealtime({ token, onUpdate: load });

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <h2>{t('All orders')}</h2>
          <p>{t('Track every piece that passes through your shop.')}</p>
        </div>
        <Link to="/app/orders/new" className="btn btn-primary btn-sm"><Plus size={16} /> New Order</Link>
      </div>

      <div className="card filter-card">
        <div className="filter-top">
          <div className="search-box">
            <Search size={17} />
            <input
              value={search}
              onChange={(event) => { setSearch(event.target.value); setPage(1); }}
              placeholder={t('Search by order no, customer or phone…')}
            />
          </div>
          <button className="icon-btn" onClick={load} title="Refresh"><RefreshCw size={17} /></button>
        </div>
        <div className="filter-pills">
          {FILTERS.map((value) => (
            <button
              key={value || 'all'}
              className={`pill ${status === value ? 'active' : ''}`}
              onClick={() => { setStatus(value); setPage(1); }}
            >
              {value ? t(STATUS_LABELS[value]) : t('All')}
            </button>
          ))}
        </div>
      </div>

      <div className="card">
        {loading && !orders.length ? (
          <Loader full label="Loading orders…" />
        ) : error ? (
          <div className="card error-card"><p>{error}</p><button className="btn btn-outline btn-sm" onClick={load}>Retry</button></div>
        ) : orders.length === 0 ? (
          <EmptyState
            icon={PackageOpen}
            title="No orders found"
            description="Try a different filter, or create your first order."
            action={<Link to="/app/orders/new" className="btn btn-primary btn-sm"><Plus size={16} /> New Order</Link>}
          />
        ) : (
          <>
            <OrderTable orders={orders} />
            <div className="pagination">
              <span>Showing {orders.length} of {total} orders</span>
              <div>
                <button className="btn btn-ghost btn-sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}><ChevronLeft size={16} /></button>
                <span className="page-indicator">Page {page} of {totalPages}</span>
                <button className="btn btn-ghost btn-sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}><ChevronRight size={16} /></button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
