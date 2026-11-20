import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Html5Qrcode } from 'html5-qrcode';
import { ScanLine, Camera, Keyboard, ArrowRight, CheckCircle2, Clock3, Package, UserRound } from 'lucide-react';
import api from '../api/client';
import { useToast } from '../context/ToastContext';
import { useLanguage } from '../context/LanguageContext';
import { StatusBadge, PaymentBadge } from '../components/StatusBadge';
import { STATUS_LABELS, STATUS_ORDER, money, formatDateTime } from '../utils/helpers';

function extractToken(text) {
  try {
    const url = new URL(text);
    const parts = url.pathname.split('/').filter(Boolean);
    const index = parts.indexOf('t');
    if (index >= 0 && parts[index + 1]) return parts[index + 1];
  } catch { /* not a URL */ }
  const match = String(text).match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
  return match ? match[0] : null;
}

export default function Scan() {
  const [mode, setMode] = useState('camera');
  const [scanning, setScanning] = useState(false);
  const [manual, setManual] = useState('');
  const [order, setOrder] = useState(null);
  const [busy, setBusy] = useState(false);
  const [cameraError, setCameraError] = useState('');
  const [payAmount, setPayAmount] = useState('');
  const [payMethod, setPayMethod] = useState('Cash');
  const [paying, setPaying] = useState(false);
  const toast = useToast();
  const { t } = useLanguage();

  const lookup = useCallback(async (raw) => {
    const token = extractToken(raw);
    if (!token) { toast.error('That QR code does not contain a WashTag receipt.'); return false; }
    setBusy(true);
    try {
      const { data } = await api.get(`/orders/lookup/${token}`);
      setOrder(data);
      setScanning(false);
      toast.success(`Found order #${data.orderNumber}.`);
      return true;
    } catch (error) {
      toast.error(error.message);
      return false;
    } finally {
      setBusy(false);
    }
  }, [toast]);

  // Keep the scanner stable: a toast rerender must not restart the camera.
  const lookupRef = useRef(lookup);
  lookupRef.current = lookup;

  useEffect(() => {
    if (!scanning || mode !== 'camera') return undefined;
    let scanner;
    let cancelled = false;
    try {
      scanner = new Html5Qrcode('qr-reader', { verbose: false });
      scanner.start(
        { facingMode: 'environment' },
        { fps: 10, qrbox: 230 },
        (decoded) => { setScanning(false); lookupRef.current(decoded); },
        () => {}
      ).then(() => {
        if (cancelled) scanner.stop().then(() => scanner.clear()).catch(() => {});
      }).catch(() => {
        if (cancelled) return;
        setCameraError('Camera unavailable. Check browser permissions or enter the code manually.');
        setScanning(false);
      });
    } catch {
      setCameraError('Could not start the camera. Use manual entry instead.');
      setScanning(false);
    }
    return () => {
      cancelled = true;
      try {
        const stopping = scanner?.stop?.();
        if (stopping?.then) stopping.then(() => scanner.clear()).catch(() => {});
      } catch { /* ignore */ }
    };
  }, [scanning, mode]);

  const updateStatus = async (status) => {
    setBusy(true);
    try {
      const { data } = await api.patch(`/orders/${order.id}/status`, { status });
      setOrder(data);
      toast.success(`Updated to ${STATUS_LABELS[status]}.`);
    } catch (error) {
      toast.error(error.message);
    } finally {
      setBusy(false);
    }
  };

  const recordPayment = async (event) => {
    event.preventDefault();
    if (!order || paying) return;
    setPaying(true);
    try {
      const { data } = await api.post(`/orders/${order.id}/payment`, {
        amount: Number(payAmount), method: payMethod
      });
      setOrder(data);
      setPayAmount('');
      toast.success('Payment recorded.');
    } catch (error) {
      toast.error(error.message);
    } finally { setPaying(false); }
  };

  const remaining = order && order.status !== 'Cancelled'
    ? STATUS_ORDER.slice(STATUS_ORDER.indexOf(order.status) + 1)
    : [];

  return (
    <div className="page-stack scan-page">
      <div className="page-heading">
        <div>
          <h2>{t('Scan a QR receipt')}</h2>
          <p>{t("Point your camera at a customer's QR, or enter the code manually.")}</p>
        </div>
      </div>

      <div className="scan-tabs">
        <button className={`pill ${mode === 'camera' ? 'active' : ''}`} onClick={() => { setMode('camera'); setCameraError(''); }}>
          <Camera size={16} /> {t('Camera')}
        </button>
        <button className={`pill ${mode === 'manual' ? 'active' : ''}`} onClick={() => { setMode('manual'); setScanning(false); }}>
          <Keyboard size={16} /> {t('Enter code')}
        </button>
      </div>

      <div className="scan-grid">
        <section className="card scanner-card">
          {mode === 'camera' ? (
            <>
              <div id="qr-reader" className="qr-reader" />
              {scanning && <div className="scan-frame" />}
              {!scanning && (
                <div className="scanner-placeholder">
                  <ScanLine size={38} />
                  <h3>Ready to scan</h3>
                  <p>Start the camera and show a WashTag QR to the lens.</p>
                  <button className="btn btn-primary" onClick={() => { setCameraError(''); setScanning(true); }}>
                    <Camera size={17} /> Start camera
                  </button>
                </div>
              )}
              {cameraError && <p className="scan-error">{cameraError}</p>}
            </>
          ) : (
            <form className="manual-form" onSubmit={(event) => { event.preventDefault(); lookup(manual); }}>
              <div className="manual-icon"><Keyboard size={30} /></div>
              <h3>Enter the QR code</h3>
              <p>Paste the tracking link or the code printed under the QR.</p>
              <label className="field">
                <span>Code or link</span>
                <input value={manual} onChange={(e) => setManual(e.target.value)} placeholder="https://…/t/xxxx-xxxx…" required />
              </label>
              <button className="btn btn-primary btn-block" disabled={busy}><ArrowRight size={16} /> Find order</button>
            </form>
          )}
        </section>

        <section className="card scan-result">
          <div className="card-head"><div><h3>Scan result</h3><p>The order will appear here</p></div></div>
          {!order ? (
            <div className="empty-state compact">
              <Package size={26} />
              <p>Scan a receipt to see its details and update the status instantly.</p>
            </div>
          ) : (
            <div className="scan-order">
              <div className="scan-order-head">
                <div>
                  <span className="mono strong">#{order.orderNumber}</span>
                  <h3>{order.customerName}</h3>
                  <p>{order.customerPhone}</p>
                </div>
                <StatusBadge status={order.status} />
              </div>
              <div className="scan-meta">
                <div><Package size={16} /> {order.itemCount} item(s)</div>
                <div><UserRound size={16} /> {money(order.totalAmount)}</div>
                <div><Clock3 size={16} /> {formatDateTime(order.createdAt)}</div>
              </div>
              <PaymentBadge status={order.paymentStatus} />
              <div className="scan-actions">
                {remaining.length ? remaining.map((status) => (
                  <button key={status} className={`btn btn-sm btn-block ${status === 'Delivered' ? 'btn-primary' : 'btn-outline'}`} disabled={busy} onClick={() => updateStatus(status)}>
                    {status === 'Delivered' ? <CheckCircle2 size={15} /> : <Clock3 size={15} />}
                    {status === 'Delivered' ? 'Mark delivered' : `Move to ${status === 'ReadyForPickup' && order.deliveryMode === 'Delivery' ? 'Out for delivery' : STATUS_LABELS[status]}`}
                  </button>
                )) : <p className="muted">This order is complete.</p>}
                {order.status !== 'Cancelled' && order.paymentStatus !== 'Paid' && (
                  <form className="scan-payment" onSubmit={recordPayment}>
                    <label className="field"><span>Payment amount (৳)</span>
                      <input type="number" min="0.01" step="0.01" max={Math.max(0, order.totalAmount - (order.paidAmount || 0))}
                        value={payAmount} onChange={(e) => setPayAmount(e.target.value)} placeholder="0.00" required />
                    </label>
                    <button className="btn btn-success btn-sm btn-block" type="submit" disabled={paying || !payAmount}>{paying ? 'Saving…' : 'Record payment'}</button>
                  </form>
                )}
                <Link to={`/print/${order.publicToken}`} className="text-link">Print receipt <ArrowRight size={15} /></Link>
                <Link to={`/app/orders/${order.id}`} className="text-link">Open full details <ArrowRight size={15} /></Link>
              </div>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
