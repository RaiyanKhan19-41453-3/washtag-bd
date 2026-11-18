import { STATUS_LABELS, PAYMENT_LABELS } from '../utils/helpers';

export function StatusBadge({ status, dot = true }) {
  return (
    <span className={`badge status-${status}`}>
      {dot && <i className="badge-dot" />}
      {STATUS_LABELS[status] || status}
    </span>
  );
}

export function PaymentBadge({ status }) {
  return <span className={`badge payment-${status}`}>{PAYMENT_LABELS[status] || status}</span>;
}
