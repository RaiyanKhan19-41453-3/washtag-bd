import { useCallback, useEffect, useRef, useState } from 'react';
import { CreditCard, ReceiptText, CheckCircle2, CalendarDays, BadgeCheck, AlertCircle } from 'lucide-react';
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
  const { user, setShop } = useAuth();
  const toast = useToast();
  const [data, setData] = useState(null);
  const [loadError, setLoadError] = useState('');
  const [plan, setPlan] = useState('');
  const [paying, setPaying] = useState(null);
  const [method, setMethod] = useState('Cash');
  const [reference, setReference] = useState('');
  const [busy, setBusy] = useState(false);
  const isOwner = user?.role === 'Owner';

  const lastStatusRef = useRef(null);
  const planDirtyRef = useRef(false);

  const load = useCallback(() => {
    setLoadError('');
    api.get('/billing').then(({ data: result }) => {
      // Seed the baseline only once so a webhook renewal before the first poll
      // is still detected; later loads must not erase an already-seen status.
      if (lastStatusRef.current == null) lastStatusRef.current = result.status;
      setData(result);
      setPlan(result.plan);
      planDirtyRef.current = false;
    })
      .catch((error) => { setLoadError(error.message); toast.error(error.message); });
  }, [toast]);

  useEffect(() => { load(); }, [load]);

  const refreshShop = useCallback(async () => {
    try { const { data } = await api.get('/auth/me'); setShop(data.shop); }
    catch { /* keep the previous shop state */ }
  }, [setShop]);

  // A verified gateway webhook settles invoices server-side; poll so a locked
  // owner sees the renewed state without a manual reload.
  useEffect(() => {
    const timer = window.setInterval(async () => {
      try {
        const { data } = await api.get('/billing');
        const statusChanged = lastStatusRef.current != null && lastStatusRef.current !== data.status;
        lastStatusRef.current = data.status;
        setData(data);
        setLoadError('');
        // Preserve an unsaved dropdown selection, but re-sync when untouched so
        // an admin's plan change while this page is open is still reflected.
        if (!planDirtyRef.current) setPlan(data.plan);
        if (statusChanged) void refreshShop();
      } catch { /* transient poll failure — keep the last state */ }
    }, 30000);
    return () => window.clearInterval(timer);
  }, [refreshShop]);

  const changePlan = async () => {
    setBusy(true);
    try {
      const { data: result } = await api.put('/billing/plan', { plan });
      setData(result);
      planDirtyRef.current = false;
      await refreshShop();
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
      await refreshShop();
      load();
    } catch (error) { toast.error(error.message); }
    finally { setBusy(false); }
  };

  if (loadError) {
    return (
      <div className="card error-card">
        <p>{loadError}</p>
        <button className="btn btn-outline btn-sm" onClick={load}>Retry</button>
      </div>
    );
  }
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
            <div><ReceiptText size={16} /><span>Subscription due: <strong>{money(data.outstanding)}</strong></span></div>
          </div>
          {data.status === 'Cancelled' && (
            <div className="subscription-banner">
              <AlertCircle size={17} /> <span>This plan was cancelled by the platform admin. Paying an invoice cannot restore access — contact support to have it restored.</span>
            </div>
          )}
          {isOwner && data.status !== 'Cancelled' && (
            <div className="plan-change">
              <label className="field">
                <span>Change plan</span>
                <select value={plan} onChange={(e) => { planDirtyRef.current = true; setPlan(e.target.value); }}>
                  {PLANS.map((item) => <option key={item.name} value={item.name}>{item.name} — ৳{item.price}/mo</option>)}
                </select>
              </label>
              <button className="btn btn-primary" onClick={changePlan} disabled={busy || plan === data.plan}>Update plan</button>
            </div>
          )}
        </section>

        <section className="card">
          <div className="card-head"><div><h3>Invoices</h3><p>{data.invoices.length} billing record(s)</p></div></div>
          {!data.canSelfSettle && (
            <p className="tiny-note">Invoice settlement requires platform admin approval. Contact support to confirm a payment.</p>
          )}
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
                      {invoice.status !== 'Paid' && isOwner && data.status !== 'Cancelled' && data.canSelfSettle && (
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
          <p className="tiny-note">Record a manual settlement here. Verified gateway payments settle invoices automatically via webhook.</p>
        </form>
      </Modal>
    </div>
  );
}
