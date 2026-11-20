import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  ArrowLeft, Package, UserRound, Phone, CalendarDays, StickyNote,
  QrCode, Copy, ExternalLink, CheckCircle2, XCircle, CircleDollarSign,
  Printer, Clock3, RefreshCw, Store
} from 'lucide-react';
import api from '../api/client';
import { useRealtime } from '../hooks/useRealtime';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import { useToast } from '../context/ToastContext';
import QRCode from '../components/QRCode';
import Timeline from '../components/Timeline';
import Modal from '../components/Modal';
import Loader from '../components/Loader';
import EmptyState from '../components/EmptyState';
import { StatusBadge, PaymentBadge } from '../components/StatusBadge';
import {
  STATUS_LABELS, STATUS_ORDER, PAYMENT_METHOD_LABELS,
  money, formatDateTime, formatDate, trackingUrl
} from '../utils/helpers';

export default function OrderDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const { t } = useLanguage();
  const { user } = useAuth();
  const [order, setOrder] = useState(null);
  const [loadedId, setLoadedId] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [paymentOpen, setPaymentOpen] = useState(false);
  const [payment, setPayment] = useState({ amount: '', method: 'Cash', reference: '' });
  const token = localStorage.getItem('washtag_token');

  const load = useCallback(() => {
    api.get(`/orders/${id}`)
      .then(({ data }) => { setOrder(data); setLoadedId(id); setError(''); })
      .catch((err) => setError(err.message));
  }, [id]);

  useEffect(() => {
    load();
    const timer = window.setInterval(load, 60000);
    return () => window.clearInterval(timer);
  }, [load]);

  useRealtime({ token, onUpdate: load });

  const updateStatus = async (status) => {
    setBusy(true);
    try {
      await api.patch(`/orders/${order.id}/status`, { status });
      toast.success(`Status updated to ${STATUS_LABELS[status]}.`);
      load();
    } catch (err) {
      toast.error(err.message);
    } finally {
      setBusy(false);
    }
  };

  const submitPayment = async (event) => {
    event.preventDefault();
    // Guard synchronously: disabled={busy} applies a render later, so a quick
    // double-click could otherwise record two partial payments.
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    try {
      await api.post(`/orders/${order.id}/payment`, {
        amount: Number(payment.amount),
        method: payment.method,
        reference: payment.reference || null
      });
      toast.success('Payment recorded.');
      setPaymentOpen(false);
      setPayment({ amount: '', method: 'Cash', reference: '' });
      load();
    } catch (err) {
      toast.error(err.message);
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };

  const cancelOrder = async () => {
    if (!window.confirm('Cancel this order? This cannot be undone.')) return;
    try {
      await api.delete(`/orders/${order.id}`);
      toast.success('Order cancelled.');
      navigate('/app/orders');
    } catch (err) {
      toast.error(err.message);
    }
  };

  const copyLink = async () => {
    try {
      await navigator.clipboard.writeText(order.trackingUrl || trackingUrl(order.publicToken));
      toast.success('Tracking link copied.');
    } catch {
      toast.info('Copy this link: ' + (order.trackingUrl || trackingUrl(order.publicToken)));
    }
  };

  if (!order || loadedId !== id) {
    if (error) {
      return (
        <div className="card error-card">
          <p>{error}</p>
          <div className="form-actions"><button className="btn btn-primary" onClick={load}>Retry</button></div>
        </div>
      );
    }
    return <Loader full label="Loading order…" />;
  }
  if (error && !order) {
    return (
      <div className="card error-card">
        <p>{error}</p>
        <button className="btn btn-outline btn-sm" onClick={load}>Retry</button>
      </div>
    );
  }

  const remaining = order.status === 'Cancelled'
    ? []
    : STATUS_ORDER.slice(STATUS_ORDER.indexOf(order.status) + 1);
  const balance = Math.max(0, order.totalAmount - order.paidAmount);
  const link = order.trackingUrl || trackingUrl(order.publicToken);

  return (
    <div className="page-stack">
      <button className="btn btn-ghost btn-sm back-link" onClick={() => navigate('/app/orders')}>
        <ArrowLeft size={16} /> {t('Back to orders')}
      </button>

      <div className="detail-header">
        <div>
          <div className="detail-title-row">
            <h2>{t('Order')} #{order.orderNumber}</h2>
            <StatusBadge status={order.status} />
            <PaymentBadge status={order.paymentStatus} />
          </div>
          <p>Created {formatDateTime(order.createdAt)} · Last updated {formatDateTime(order.updatedAt)}</p>
        </div>
        <button className="btn btn-outline btn-sm" onClick={load}><RefreshCw size={15} /> {t('Refresh')}</button>
      </div>

      <div className="detail-grid">
        <div className="detail-main">
          <section className="card">
            <div className="card-head"><div><h3>{t('Items')}</h3><p>{order.itemCount} {t('Items').toLowerCase()}</p></div></div>
            <div className="table-scroll">
              <table className="data-table">
                <thead><tr><th>{t('Item')}</th><th>{t('Service')}</th><th className="align-right">{t('Qty')}</th><th className="align-right">{t('Price')}</th><th className="align-right">{t('Total')}</th></tr></thead>
                <tbody>
                  {order.items.map((item) => (
                    <tr key={item.id}>
                      <td className="strong">{item.name}{item.notes && <div className="cell-secondary">{item.notes}</div>}</td>
                      <td><span className="soft-chip">{item.service}</span></td>
                      <td className="align-right">{item.quantity}</td>
                      <td className="align-right muted">{money(item.unitPrice)}</td>
                      <td className="align-right strong">{money(item.lineTotal)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr><td colSpan="4" className="align-right">{t('Order total')}</td><td className="align-right strong">{money(order.totalAmount)}</td></tr>
                </tfoot>
              </table>
            </div>
          </section>

          <section className="card">
            <div className="card-head"><div><h3>{t('Status timeline')}</h3><p>{t('Every step, recorded')}</p></div></div>
            <Timeline events={order.statusHistory} />
          </section>

          <section className="card">
            <div className="card-head"><div><h3>{t('Payment history')}</h3><p>{money(order.paidAmount)} {t('Paid')} · {money(balance)} {t('outstanding')}</p></div></div>
            {order.payments.length ? (
              <div className="payment-list">
                {order.payments.map((paymentItem) => (
                  <div className="payment-row" key={paymentItem.id}>
                    <span className="payment-icon"><CircleDollarSign size={17} /></span>
                    <div>
                      <strong>{money(paymentItem.amount)}</strong>
                      <span>{PAYMENT_METHOD_LABELS[paymentItem.method]}{paymentItem.reference ? ` · ${paymentItem.reference}` : ''}</span>
                    </div>
                    <time>{formatDateTime(paymentItem.createdAt)}</time>
                  </div>
                ))}
              </div>
            ) : <p className="muted">No payments recorded yet.</p>}
          </section>
        </div>

        <aside className="detail-side">
          <section className="card qr-card">
            <div className="card-head"><div><h3>{t('QR receipt')}</h3><p>{t('Customer scans this for live status')}</p></div></div>
            <div className="qr-display"><QRCode value={link} dataUrl={order.qrDataUrl} size={218} /></div>
            <div className="qr-order-no">#{order.orderNumber}</div>
            <div className="qr-actions">
              <button className="btn btn-outline btn-sm" onClick={copyLink}><Copy size={15} /> {t('Copy link')}</button>
              <Link className="btn btn-outline btn-sm" to={`/print/${order.publicToken}`}><Printer size={15} /> {t('Print receipt')}</Link>
              <a className="btn btn-outline btn-sm" href={link} target="_blank" rel="noreferrer"><ExternalLink size={15} /> {t('Preview')}</a>
            </div>
          </section>

          <section className="card">
            <div className="card-head"><div><h3>{t('Customer')}</h3></div></div>
            <div className="info-list">
              <div><UserRound size={16} /><span>{order.customerName}</span></div>
              <div><Phone size={16} /><span>{order.customerPhone}</span></div>
              <div><CalendarDays size={16} /><span>{order.deliveryMode === 'Delivery' ? (t('Delivery address') || 'Delivery address') : t('Expected pickup')}: {order.deliveryMode === 'Delivery' ? (order.deliveryAddress || '—') : formatDate(order.dueDate)}</span></div>
              <div><Store size={16} /><span>{order.deliveryMode === 'Delivery' ? `${t('Delivery address')}: ${order.deliveryAddress || '—'}` : t('Customer pickup')}</span></div>
              {order.notes && <div><StickyNote size={16} /><span>{order.notes}</span></div>}
            </div>
          </section>

          <section className="card action-card">
            <div className="card-head"><div><h3>{t('Update order')}</h3><p>{t('Move it along the workflow')}</p></div></div>
            {remaining.length ? (
              <div className="action-buttons">
                {remaining.map((status) => (
                  <button key={status} className={`btn btn-sm ${status === 'Delivered' ? 'btn-primary' : 'btn-outline'} btn-block`} disabled={busy} onClick={() => updateStatus(status)}>
                    {status === 'Delivered' ? <CheckCircle2 size={15} /> : <Clock3 size={15} />}
                    {status === 'Delivered' ? t('Mark as delivered') : `${t('Move to')} ${t(STATUS_LABELS[status])}`}
                  </button>
                ))}
              </div>
            ) : (
              <div className="completed-note">
                <CheckCircle2 size={20} /> {order.status === 'Cancelled' ? 'This order was cancelled.' : 'This order is complete.'}
              </div>
            )}
            {!['Delivered', 'Cancelled'].includes(order.status) && (
              <button className="btn btn-success btn-sm btn-block" onClick={() => { setPayment({ amount: balance > 0 ? balance : '', method: 'Cash', reference: '' }); setPaymentOpen(true); }}>
                <CircleDollarSign size={15} /> {t('Record payment')}
              </button>
            )}
            {user?.role === 'Owner' && !['Delivered', 'Cancelled'].includes(order.status) && (
              <button className="btn btn-ghost btn-sm btn-block danger-text" onClick={cancelOrder}><XCircle size={15} /> {t('Cancel order')}</button>
            )}
          </section>
        </aside>
      </div>

      <Modal
        open={paymentOpen}
        onClose={() => setPaymentOpen(false)}
        title="Record payment"
        subtitle={`Outstanding balance: ${money(balance)}`}
        footer={<><button className="btn btn-ghost btn-sm" onClick={() => setPaymentOpen(false)}>Cancel</button><button form="payment-form" type="submit" className="btn btn-primary btn-sm" disabled={busy}>{busy ? 'Saving…' : 'Save payment'}</button></>}
      >
        <form id="payment-form" onSubmit={submitPayment} className="form-stack">
          <label className="field">
            <span>Amount (BDT)</span>
            <input type="number" min="1" step="1" value={payment.amount} onChange={(e) => setPayment((p) => ({ ...p, amount: e.target.value }))} placeholder="0" required autoFocus />
          </label>
          <label className="field">
            <span>Method</span>
            <select value={payment.method} onChange={(e) => setPayment((p) => ({ ...p, method: e.target.value }))}>
              {Object.entries(PAYMENT_METHOD_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
            </select>
          </label>
          <label className="field">
            <span>Reference <em>(optional)</em></span>
            <input value={payment.reference} onChange={(e) => setPayment((p) => ({ ...p, reference: e.target.value }))} placeholder="Transaction ID" />
          </label>
        </form>
      </Modal>
    </div>
  );
}
