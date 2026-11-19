import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Printer, ArrowLeft } from 'lucide-react';
import api from '../api/client';
import QRCode from '../components/QRCode';
import Loader from '../components/Loader';
import { trackingUrl, formatDate } from '../utils/helpers';

export default function PrintReceipt() {
  const { token } = useParams();
  const [data, setData] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    api.get(`/public/track/${token}`)
      .then(({ data: result }) => setData(result))
      .catch((err) => setError(err.message));
  }, [token]);

  if (error) {
    return (
      <div className="print-wrapper">
        <div className="card no-print">
          <h2>Receipt not found</h2>
          <p className="muted">{error}</p>
          <Link to="/app/orders" className="btn btn-outline btn-sm"><ArrowLeft size={15} /> Back to orders</Link>
        </div>
      </div>
    );
  }
  if (!data) return <Loader full label="Preparing receipt…" />;

  const { shop, order } = data;

  return (
    <div className="print-wrapper">
      <div className="print-toolbar no-print">
        <Link to={`/app/orders`} className="btn btn-ghost btn-sm"><ArrowLeft size={15} /> Back to orders</Link>
        <button className="btn btn-primary btn-sm" onClick={() => window.print()}>
          <Printer size={16} /> Print receipt
        </button>
      </div>

      <div className="receipt-paper">
        <div className="receipt-shop">
          <strong>{shop.name}</strong>
          {(shop.address || shop.city) && <span>{[shop.address, shop.city].filter(Boolean).join(', ')}</span>}
          {shop.phone && <span>Ph: {shop.phone}</span>}
        </div>

        <div className="receipt-divider" />

        <div className="receipt-line"><span>Order</span><strong>#{order.orderNumber}</strong></div>
        <div className="receipt-line"><span>Customer</span><strong>{order.customerName}</strong></div>
        <div className="receipt-line"><span>Received</span><strong>{formatDate(order.createdAt)}</strong></div>
        <div className="receipt-line"><span>Pickup</span><strong>{order.dueDate ? formatDate(order.dueDate) : '—'}</strong></div>

        <div className="receipt-divider" />

        <div className="receipt-items">
          {order.items.map((item) => (
            <div className="receipt-item" key={item.id}>
              <span>{item.quantity} × {item.name}{item.service ? ` · ${item.service}` : ''}</span>
              <span>৳{item.lineTotal}</span>
            </div>
          ))}
        </div>

        <div className="receipt-divider" />
        <div className="receipt-total"><span>TOTAL</span><strong>৳{order.items.reduce((sum, item) => sum + item.lineTotal, 0)}</strong></div>
        <div className="receipt-line receipt-payment"><span>Payment</span><strong>{order.paymentStatus}</strong></div>

        <div className="receipt-qr">
          <QRCode value={order.trackingUrl || trackingUrl(order.token)} size={150} />
          <p>Scan for live status</p>
          <small>{order.trackingUrl || trackingUrl(order.token)}</small>
        </div>

        <div className="receipt-divider" />
        <div className="receipt-footer">Thank you! · Powered by WashTag BD</div>
      </div>
    </div>
  );
}
