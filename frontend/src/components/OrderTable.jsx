import { Link } from 'react-router-dom';
import { ArrowUpRight } from 'lucide-react';
import { StatusBadge, PaymentBadge } from './StatusBadge';
import { money, formatDate } from '../utils/helpers';

export default function OrderTable({ orders = [], showCustomer = true }) {
  if (!orders.length) return <p className="muted">No orders to show.</p>;

  return (
    <div className="table-scroll">
      <table className="data-table">
        <thead>
          <tr>
            <th>Order</th>
            {showCustomer && <th>Customer</th>}
            <th>Items</th>
            <th>Status</th>
            <th>Payment</th>
            <th className="align-right">Total</th>
            <th>Date</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {orders.map((order) => (
            <tr key={order.id}>
              <td><span className="mono strong">#{order.orderNumber}</span></td>
              {showCustomer && (
                <td>
                  <div className="cell-primary">{order.customerName}</div>
                  <div className="cell-secondary">{order.customerPhone}</div>
                </td>
              )}
              <td>{order.itemCount}</td>
              <td><StatusBadge status={order.status} /></td>
              <td><PaymentBadge status={order.paymentStatus} /></td>
              <td className="align-right strong">{money(order.totalAmount)}</td>
              <td className="muted">{formatDate(order.createdAt)}</td>
              <td className="align-right">
                <Link to={`/app/orders/${order.id}`} className="table-link" aria-label="Open order">
                  <ArrowUpRight size={17} />
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
