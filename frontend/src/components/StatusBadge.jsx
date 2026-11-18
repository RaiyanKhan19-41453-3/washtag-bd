import { STATUS_LABELS, PAYMENT_LABELS } from '../utils/helpers';
import { useLanguage } from '../context/LanguageContext';

export function StatusBadge({ status, dot = true }) {
  const { t } = useLanguage();
  return (
    <span className={`badge status-${status}`}>
      {dot && <i className="badge-dot" />}
      {t(STATUS_LABELS[status] || status)}
    </span>
  );
}

export function PaymentBadge({ status }) {
  const { t } = useLanguage();
  return <span className={`badge payment-${status}`}>{t(PAYMENT_LABELS[status] || status)}</span>;
}
