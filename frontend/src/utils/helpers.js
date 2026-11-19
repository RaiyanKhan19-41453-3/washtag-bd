export const STATUS_LABELS = {
  Received: 'Received',
  Washing: 'Washing',
  Ironing: 'Ironing',
  ReadyForPickup: 'Ready for Pickup',
  Delivered: 'Delivered',
  Cancelled: 'Cancelled'
};

export const STATUS_ORDER = ['Received', 'Washing', 'Ironing', 'ReadyForPickup', 'Delivered'];

export const PAYMENT_LABELS = {
  Unpaid: 'Unpaid',
  Partial: 'Partial',
  Paid: 'Paid'
};

export const PAYMENT_METHOD_LABELS = {
  Cash: 'Cash',
  Card: 'Card',
  Bkash: 'bKash',
  Nagad: 'Nagad',
  BankTransfer: 'Bank Transfer',
  Other: 'Other'
};

export const SERVICES = ['Wash & Fold', 'Wash & Iron', 'Dry Clean', 'Iron Only', 'Tailoring', 'Repair'];

export function money(value) {
  const n = Number(value || 0);
  return `৳${n.toLocaleString('en-BD', { minimumFractionDigits: 0, maximumFractionDigits: 2 })}`;
}

export function formatDate(value, options = {}) {
  if (!value) return '—';
  return new Date(value).toLocaleDateString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    ...options
  });
}

export function formatDateTime(value) {
  if (!value) return '—';
  return new Date(value).toLocaleString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  });
}

export function timeAgo(value) {
  if (!value) return '';
  const diff = Date.now() - new Date(value).getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  return formatDate(value);
}

export function nextStatus(status) {
  const index = STATUS_ORDER.indexOf(status);
  return index >= 0 && index < STATUS_ORDER.length - 1 ? STATUS_ORDER[index + 1] : null;
}

export function initials(name) {
  return (name || '?')
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');
}

export function trackingUrl(token) {
  return `${window.location.origin}/track/${token}`;
}

export function shopLocked(shop) {
  if (!shop) return false;
  return !shop.isActive ||
    ['PastDue', 'Cancelled'].includes(shop.subscriptionStatus) ||
    (shop.subscriptionEndsAt && new Date(shop.subscriptionEndsAt) <= new Date());
}

export function greeting() {
  const hour = new Date().getHours();
  if (hour < 12) return 'Good morning';
  if (hour < 17) return 'Good afternoon';
  return 'Good evening';
}
