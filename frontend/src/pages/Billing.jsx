import { useCallback, useEffect, useState } from 'react';
import { CreditCard, ReceiptText, CheckCircle2, CalendarDays, BadgeCheck } from 'lucide-react';
import api from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import Modal from '../components/Modal';
import Loader from '../components/Loader';
import { money, formatDate, PAYMENT_METHOD_LABELS } from '../utils/helpers';

const PLANS = [
  { name: 'Starter', price: 500 },
  { name: 'Growth', price: 800 },
  { name: 'Pro', price: 1000 }
];

export default function Billing() {
  const { user } = useAuth();
  const toast = useToast();
  const [data, setData] = useState(null);
  const [plan, setPlan] = useState('');
  const [paying, setPaying] = useState(null);
  const [method, setMethod] = useState('Cash');
  const [reference, setReference] = useState('');
  const [busy, setBusy] = useState(false);
  const isOwner = user?.role === 'Owner';

  const load = useCallback(() => {
    api.get('/billing').then(({ data: result }) => { setData(result); setPlan(result.plan); })
      .catch((error) => toast.error(error.message));
  }, [toast]);

  useEffect(() => { load(); }, [load]);

  const changePlan = async () => {
    setBusy(true);
    try {
      const { data: result } = await api.put('/billing/plan', { plan });
      setData(result);
      toast.success(`Plan changed to ${result.plan}.`);
    } catch (error) { toast.error(error.message); }
    finally { setBusy(false); }
  };

  const payInvoice = async (event) => {
    event.preventDefault();
    setBusy(true);
    try {
      await api.post(`/billing/invoices/${paying.id}/pay`, { method, reference: reference || null });
      toast.success('Invoice marked as paid.');
      setPaying(null);
      setReference('');
      load();
    } catch (error) { toast.error(error.message); }
    finally { setBusy(false); }
  };

  if (!data) return <Loader full label="Loading billing…" />;

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div><h2>Subscription & billing</h2><p>Manage your plan and settle invoices.</p></div>
      </div>

      <div className="billing-grid">
        <section className="card plan-card">
          <div className="plan-card-head">
            <div>
              <span className="eyebrow">CURRENT PLAN</span>
              <h2>{data.plan}</h2>
            </div>
            <span className={`badge ${data.status === 'Active' ? 'payment-Paid' : 'payment-Partial'}`}>
              <i className="badge-dot" /> {data.status}
            </span>
          </div>
          <div className="plan-price">{money(data.monthlyPrice)}<small>/month</small></div>
          <div className="plan-meta">
            <div><CalendarDays size={16} /><span>Started {formatDate(data.startedAt)}</span></div>
            <div><BadgeCheck size={16} /><span>Renews {formatDate(data.renewalDate)}</span></div>
            <div><ReceiptText size={16} /><span>Outstanding: <strong>{money(data.outstanding)}</strong></span></div>
          </div>
          {isOwner && (
            <div className="plan-change">
              <label className="field">
                <span>Change plan</span>
                <select value={plan} onChange={(e) => setPlan(e.target.value)}>
                  {PLANS.map((item) => <option key={item.name} value={item.name}>{item.name} — ৳{item.price}/mo</option>)}
                </select>
              </label>
              <button className="btn btn-primary" onClick={changePlan} disabled={busy || plan === data.plan}>Update plan</button>
            </div>
          )}
        </section>

        <section className="card">
          <div className="card-head"><div><h3>Invoices</h3><p>{data.invoices.length} billing record(s)</p></div></div>
          <div className="table-scroll">
            <table className="data-table">
              <thead><tr><th>Invoice</th><th>Period</th><th className="align-right">Amount</th><th>Status</th><th /></tr></thead>
              <tbody>
                {data.invoices.map((invoice) => (
                  <tr key={invoice.id}>
                    <td className="mono strong">{invoice.invoiceNumber}</td>
                    <td className="muted">{formatDate(invoice.periodFrom)} – {formatDate(invoice.periodTo)}</td>
                    <td className="align-right strong">{money(invoice.amount)}</td>
                    <td><span className={`badge payment-${invoice.status}`}>{invoice.status}</span></td>
                    <td className="align-right">
                      {invoice.status !== 'Paid' && isOwner && (
                        <button className="btn btn-outline btn-sm" onClick={() => setPaying(invoice)}><CreditCard size={14} /> Settle</button>
                      )}
                      {invoice.status === 'Paid' && <span className="paid-note"><CheckCircle2 size={14} /> {formatDate(invoice.paidAt)}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>

      <Modal
        open={!!paying}
        onClose={() => setPaying(null)}
        title="Settle invoice"
        subtitle={paying ? `${paying.invoiceNumber} · ${money(paying.amount)}` : ''}
        footer={<><button className="btn btn-ghost btn-sm" onClick={() => setPaying(null)}>Cancel</button><button form="invoice-form" type="submit" className="btn btn-primary btn-sm" disabled={busy}>Confirm payment</button></>}
      >
        <form id="invoice-form" onSubmit={payInvoice} className="form-stack">
          <label className="field"><span>Method</span><select value={method} onChange={(e) => setMethod(e.target.value)}>
            {Object.entries(PAYMENT_METHOD_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
          </select></label>
          <label className="field"><span>Reference <em>(optional)</em></span><input value={reference} onChange={(e) => setReference(e.target.value)} placeholder="bKash / bank reference" /></label>
          <p className="tiny-note">Record manual settlement here. Online gateway collection can be added later.</p>
        </form>
      </Modal>
    </div>
  );
}
