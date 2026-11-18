import { useCallback, useEffect, useState } from 'react';
import { Users, Search, Wallet, AlertCircle, Package } from 'lucide-react';
import api from '../api/client';
import { useRealtime } from '../hooks/useRealtime';
import { useToast } from '../context/ToastContext';
import StatCard from '../components/StatCard';
import EmptyState from '../components/EmptyState';
import Loader from '../components/Loader';
import { money, formatDate, initials } from '../utils/helpers';

export default function Customers() {
  const [customers, setCustomers] = useState([]);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const token = localStorage.getItem('washtag_token');
  const toast = useToast();

  const load = useCallback(() => {
    api.get('/customers')
      .then(({ data }) => { setCustomers(data); setLoading(false); })
      .catch((error) => { toast.error(error.message); setLoading(false); });
  }, [toast]);

  useEffect(() => { load(); }, [load]);
  useRealtime({ token, onUpdate: load });

  const filtered = customers.filter((customer) =>
    `${customer.name} ${customer.phone}`.toLowerCase().includes(search.toLowerCase())
  );

  const totalSpent = customers.reduce((sum, c) => sum + c.totalSpent, 0);
  const outstanding = customers.reduce((sum, c) => sum + c.unpaidAmount, 0);

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div><h2>Customers</h2><p>Everyone who has trusted your shop with their clothes.</p></div>
      </div>

      <div className="stat-grid stat-grid-three">
        <StatCard icon={Users} label="Customers" value={customers.length} hint="Unique phone numbers" tone="teal" />
        <StatCard icon={Wallet} label="Total billed" value={money(totalSpent)} hint="Across all orders" tone="emerald" />
        <StatCard icon={AlertCircle} label="Outstanding" value={money(outstanding)} hint="Yet to be collected" tone="amber" />
      </div>

      <div className="card filter-card">
        <div className="search-box">
          <Search size={17} />
          <input aria-label="Search customers" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search by name or phone…" />
        </div>
      </div>

      <div className="card">
        {loading ? <Loader full label="Loading customers…" /> : filtered.length === 0 ? (
          <EmptyState icon={Users} title="No customers yet" description="Create an order and your customers will show up here automatically." />
        ) : (
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Customer</th><th>Phone</th><th className="align-right">Orders</th>
                  <th className="align-right">Total billed</th><th className="align-right">Due</th><th>Last order</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((customer) => (
                  <tr key={customer.phone}>
                    <td>
                      <div className="person-cell"><span className="avatar avatar-sm">{initials(customer.name)}</span><strong>{customer.name}</strong></div>
                    </td>
                    <td className="muted">{customer.phone}</td>
                    <td className="align-right">{customer.orderCount}</td>
                    <td className="align-right strong">{money(customer.totalSpent)}</td>
                    <td className="align-right">{customer.unpaidAmount > 0 ? <span className="due-text">{money(customer.unpaidAmount)}</span> : <span className="muted">—</span>}</td>
                    <td className="muted">{formatDate(customer.lastOrderAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
