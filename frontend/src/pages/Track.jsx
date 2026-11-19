import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Shirt, MapPin, CheckCircle2, Clock3, PackageCheck,
  SearchX, RefreshCw, Sparkles, Receipt, Wallet
} from 'lucide-react';
import api from '../api/client';
import { useLanguage } from '../context/LanguageContext';
import LanguageToggle from '../components/LanguageToggle';
import { useRealtime } from '../hooks/useRealtime';
import Timeline from '../components/Timeline';
import Loader from '../components/Loader';
import { PaymentBadge } from '../components/StatusBadge';
import { STATUS_LABELS, STATUS_ORDER, formatDateTime, formatDate } from '../utils/helpers';

const STATUS_MESSAGES = {
  Received: 'We have your clothes safely. They will enter the wash queue soon.',
  Washing: 'Your clothes are being cleaned right now.',
  Ironing: 'Clean and fresh — we are pressing them to perfection.',
  ReadyForPickup: 'All done! Your order is packed and waiting for you.',
  Delivered: 'Delivered. Thank you for trusting us!',
  Cancelled: 'This order was cancelled. Please contact the shop for details.'
};

const STATUS_ICONS = {
  Received: Clock3,
  Washing: Sparkles,
  Ironing: Shirt,
  ReadyForPickup: PackageCheck,
  Delivered: CheckCircle2,
  Cancelled: SearchX
};

export default function Track() {
  const { token } = useParams();
  const { t } = useLanguage();
  const [data, setData] = useState(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    api.get(`/public/track/${token}`)
      .then(({ data: result }) => { setData(result); setError(''); })
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false));
  }, [token]);

  useEffect(() => {
    load();
    const timer = window.setInterval(load, 30000);
    return () => window.clearInterval(timer);
  }, [load]);

  useRealtime({ group: { type: 'token', value: token }, onUpdate: load });

  if (loading && !data && !error) return <Loader full label="Checking your order…" />;

  if (error && !data) {
    return (
      <div className="track-shell">
        <div className="track-card not-found-card">
          <div className="not-found-icon"><SearchX size={34} /></div>
          <h1>We couldn't find this receipt</h1>
          <p>{error} Double-check the QR code, or ask your shop for a fresh receipt.</p>
          <button className="btn btn-outline" onClick={load}><RefreshCw size={16} /> Try again</button>
        </div>
        <p className="track-power">Powered by WashTag BD</p>
      </div>
    );
  }

  const { shop, order, timeline } = data;
  const StatusIcon = STATUS_ICONS[order.status] || Clock3;
  const currentIndex = STATUS_ORDER.indexOf(order.status);
  const isCancelled = order.status === 'Cancelled';
  const progress = isCancelled || currentIndex < 0
    ? 0
    : Math.round(((currentIndex + 1) / STATUS_ORDER.length) * 100);

  return (
    <div className="track-shell">
      <div className="track-card">
        <header className="track-header">
          <div className="track-shop-avatar"><Shirt size={22} /></div>
          <div>
            <h1>{shop.name}</h1>
            <p><MapPin size={13} /> {[shop.address, shop.city].filter(Boolean).join(', ') || 'Bangladesh'}</p>
          </div>
          <LanguageToggle compact />
        </header>

        <div className={`track-hero status-bg-${order.status}`}>
          <div className="track-hero-icon"><StatusIcon size={30} /></div>
          <span className="track-eyebrow">{t('CURRENT STATUS')}</span>
          <h2>{t(STATUS_LABELS[order.status])}</h2>
          <p>{STATUS_MESSAGES[order.status]}</p>
          <div className="track-order-no"><Receipt size={14} /> {order.orderNumber} · Dropped off {formatDate(order.createdAt)}</div>
        </div>

        <div className="track-progress">
          <div className="track-progress-head">
            <span>{t('Overall progress')}</span>
            <strong>{progress}%</strong>
          </div>
          <div className="track-progress-track">
            <div className="track-progress-fill" style={{ width: `${progress}%` }} />
          </div>
          <div className="track-progress-caption">
            {isCancelled ? t('Cancelled') : `${currentIndex + 1} / ${STATUS_ORDER.length} ${t('steps complete')}`}
          </div>
        </div>

        <div className="track-steps">
          {STATUS_ORDER.map((status, index) => (
            <div key={status} className={`track-step ${index < currentIndex ? 'done' : ''} ${index === currentIndex ? 'current' : ''}`}>
              <span className="step-dot">{index < currentIndex ? <CheckCircle2 size={13} /> : index + 1}</span>
              <span className="step-label">{t(STATUS_LABELS[status])}</span>
            </div>
          ))}
        </div>

        <section className="track-section">
          <div className="track-section-head"><h3>{t('Your items')}</h3><span>{order.items.length} {t('Items').toLowerCase()}</span></div>
          <div className="track-items">
            {order.items.map((item) => (
              <div className="track-item" key={item.id}>
                <span className="track-item-icon"><Shirt size={16} /></span>
                <div><strong>{item.name}</strong><span>{item.service}</span></div>
                <span className="track-qty">× {item.quantity}</span>
              </div>
            ))}
          </div>
        </section>

        <section className="track-section">
          <div className="track-section-head">
            <h3>{t('Payment')}</h3>
            <PaymentBadge status={order.paymentStatus} />
          </div>
          <div className="track-payment-row">
            <Wallet size={16} />
            <span>{order.paymentStatus === 'Paid' ? 'All settled — thank you!' : (order.deliveryMode === 'Delivery' ? 'Payment is collected on delivery.' : 'Settle your payment at pickup.')}</span>
          </div>
        </section>

        <section className="track-section">
          <div className="track-section-head"><h3>{t('Order journey')}</h3><span>{t('Live updates')}</span></div>
          <Timeline events={timeline} />
        </section>

        <footer className="track-footer">
          <p>{order.deliveryMode === 'Delivery' ? (t('Delivery address') || 'Delivery address') : t('Expected pickup')}: <strong>{order.deliveryMode === 'Delivery' ? (order.deliveryAddress || '—') : (order.dueDate ? formatDate(order.dueDate) : '—')}</strong></p>
          {order.deliveredAt && <p>Delivered on <strong>{formatDateTime(order.deliveredAt)}</strong></p>}
          <p>{t('Questions? Call the shop at')} <strong>{shop.phone ? <a href={`tel:${shop.phone.replace(/[^\d+]/g, '')}`}>{shop.phone}</a> : '—'}</strong></p>
        </footer>
      </div>
      <p className="track-power">{t('Powered by')} <strong>WashTag BD</strong> · {t('Live QR tracking')}</p>
    </div>
  );
}
