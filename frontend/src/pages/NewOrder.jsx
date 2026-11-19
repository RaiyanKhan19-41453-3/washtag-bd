import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { UserRound, Phone, CalendarDays, NotebookPen, Plus, Trash2, ArrowLeft, Shirt, Info } from 'lucide-react';
import api from '../api/client';
import { useToast } from '../context/ToastContext';
import { useLanguage } from '../context/LanguageContext';
import { SERVICES, money } from '../utils/helpers';

const emptyItem = () => ({ name: '', service: 'Wash & Fold', quantity: 1, unitPrice: 120, notes: '' });

export default function NewOrder() {
  const [form, setForm] = useState({
    customerName: '', customerPhone: '', notes: '', dueDate: '',
    deliveryMode: 'Pickup', deliveryAddress: '', items: [emptyItem()]
  });
  const [customers, setCustomers] = useState([]);
  const [serviceOptions, setServiceOptions] = useState([]);
  const [busy, setBusy] = useState(false);
  const toast = useToast();
  const { t } = useLanguage();
  const navigate = useNavigate();
  // The key is bound to the exact draft payload. A reload after a lost response
  // keeps the same key (safe retry), but editing into a different order gets a new one.

  useEffect(() => {
    api.get('/customers').then(({ data }) => setCustomers(data)).catch(() => {});
    api.get('/services').then(({ data }) => {
      const active = data.filter((s) => s.isActive);
      setServiceOptions(active);
      if (active.length && active[0].name === 'Wash & Fold') {
        setForm((current) => ({ ...current, items: current.items.map((it, i) => i === 0
          ? { ...it, service: active[0].name, unitPrice: active[0].defaultPrice } : it) }));
      }
    }).catch(() => {});
  }, []);

  const setField = (key, value) => setForm((current) => ({ ...current, [key]: value }));
  const setItem = (index, key, value) => setForm((current) => ({
    ...current,
    items: current.items.map((item, i) => (i === index ? { ...item, [key]: value } : item))
  }));

  const matchCustomer = (phone) => {
    setField('customerPhone', phone);
    const found = customers.find((customer) => customer.phone === phone);
    if (found) setField('customerName', found.name);
  };

  const total = form.items.reduce((sum, item) => sum + (Number(item.quantity) || 0) * (Number(item.unitPrice) || 0), 0);

  const submit = async (event) => {
    event.preventDefault();
    const validItems = form.items.filter((item) => item.name.trim());
    if (!validItems.length) { toast.error('Add at least one item.'); return; }
    if (validItems.some((item) => Number(item.quantity) < 1)) { toast.error('Quantity must be at least 1.'); return; }

    setBusy(true);
    try {
      const payload = {
        customerName: form.customerName.trim(),
        customerPhone: form.customerPhone.trim(),
        notes: form.notes?.trim() || null,
        dueDate: form.dueDate ? new Date(`${form.dueDate}T23:59:00`).toISOString() : null,
        deliveryMode: form.deliveryMode,
        deliveryAddress: form.deliveryMode === 'Delivery' ? form.deliveryAddress.trim() : null,
        items: validItems.map((item) => ({
          name: item.name.trim(),
          service: item.service,
          quantity: Number(item.quantity),
          unitPrice: Number(item.unitPrice),
          notes: item.notes?.trim() || null
        }))
      };
      const hash = JSON.stringify(payload);
      let draft = null;
      try { draft = JSON.parse(sessionStorage.getItem('washtag_order_key') || 'null'); } catch { /* ignore */ }
      if (!draft || draft.hash !== hash) {
        draft = {
          hash,
          key: crypto.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`
        };
        sessionStorage.setItem('washtag_order_key', JSON.stringify(draft));
      }
      const { data } = await api.post('/orders', payload, {
        headers: { 'Idempotency-Key': draft.key }
      });
      sessionStorage.removeItem('washtag_order_key');
      toast.success(data.replayed ? 'Order was already created. Show the QR receipt to your customer.' : 'Order created! Show the QR receipt to your customer.');
      navigate(`/app/orders/${data.id}`, { replace: true });
    } catch (error) {
      toast.error(error.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="page-stack">
      <button className="btn btn-ghost btn-sm back-link" onClick={() => navigate('/app/orders')}>
        <ArrowLeft size={16} /> Back to orders
      </button>

      <form onSubmit={submit} className="new-order-grid">
        <div className="form-main">
          <section className="card form-section">
            <div className="section-title">
              <span className="section-icon"><UserRound size={19} /></span>
              <div><h3>Customer details</h3><p>Who are we tracking this order for?</p></div>
            </div>
            <div className="form-row">
              <label className="field">
                <span>Customer name</span>
                <div className="input-with-icon"><UserRound size={17} /><input value={form.customerName} onChange={(e) => setField('customerName', e.target.value)} placeholder="e.g. Tasnim Rahman" required list="customer-names" /></div>
              </label>
              <label className="field">
                <span>Phone number</span>
                <div className="input-with-icon"><Phone size={17} /><input value={form.customerPhone} onChange={(e) => matchCustomer(e.target.value)} placeholder="+880 1xxx-xxxxxx" required list="customer-phones" /></div>
              </label>
            </div>
            <datalist id="customer-names">
              {customers.map((customer) => <option key={customer.phone} value={customer.name} />)}
            </datalist>
            <datalist id="customer-phones">
              {customers.map((customer) => <option key={customer.phone} value={customer.phone}>{customer.name}</option>)}
            </datalist>
            <div className="form-row">
              <label className="field">
                <span>Expected pickup <em>(optional)</em></span>
                <div className="input-with-icon"><CalendarDays size={17} /><input type="date" value={form.dueDate} onChange={(e) => setField('dueDate', e.target.value)} /></div>
              </label>
              <label className="field">
                <span>Notes <em>(optional)</em></span>
                <div className="input-with-icon"><NotebookPen size={17} /><input value={form.notes} onChange={(e) => setField('notes', e.target.value)} placeholder="Special instructions…" /></div>
              </label>
            </div>
            <div className="form-row">
              <label className="field">
                <span>{t('Delivery mode')}</span>
                <select value={form.deliveryMode} onChange={(e) => setField('deliveryMode', e.target.value)}>
                  <option value="Pickup">{t('Customer pickup')}</option>
                  <option value="Delivery">{t('Home delivery')}</option>
                </select>
              </label>
              <label className="field">
                <span>{t('Delivery address')} {form.deliveryMode === 'Delivery' ? <em>{t('(required)')}</em> : <em>{t('(optional)')}</em>}</span>
                <input value={form.deliveryAddress} onChange={(e) => setField('deliveryAddress', e.target.value)} placeholder="House, road, area…" />
              </label>
            </div>
          </section>

          <section className="card form-section">
            <div className="section-title section-title-between">
              <div className="section-title">
                <span className="section-icon"><Shirt size={19} /></span>
                <div><h3>Items</h3><p>Add every piece included in this order.</p></div>
              </div>
              <button type="button" className="btn btn-outline btn-sm" onClick={() => setForm((current) => ({ ...current, items: [...current.items, emptyItem()] }))}>
                <Plus size={15} /> Add item
              </button>
            </div>

            <div className="item-rows">
              {form.items.map((item, index) => (
                <div className="item-row" key={index}>
                  <label className="field field-item-name">
                    <span>Item</span>
                    <input value={item.name} onChange={(e) => setItem(index, 'name', e.target.value)} placeholder="e.g. Cotton Shirt" required />
                  </label>
                  <label className="field">
                    <span>Service</span>
                    <select value={item.service} onChange={(e) => {
                      const chosen = serviceOptions.find((s) => s.name === e.target.value);
                      setForm((current) => ({ ...current, items: current.items.map((it, i) => i === index
                        ? { ...it, service: e.target.value, unitPrice: chosen?.defaultPrice ?? it.unitPrice }
                        : it) }));
                    }}>
                      {(serviceOptions.length ? serviceOptions.map((s) => s.name) : SERVICES)
                        .map((service) => <option key={service} value={service}>{service}</option>)}
                    </select>
                  </label>
                  <label className="field field-small">
                    <span>Qty</span>
                    <input type="number" min="1" value={item.quantity} onChange={(e) => setItem(index, 'quantity', e.target.value)} />
                  </label>
                  <label className="field field-small">
                    <span>Price / pc</span>
                    <input type="number" min="0" value={item.unitPrice} onChange={(e) => setItem(index, 'unitPrice', e.target.value)} />
                  </label>
                  <button type="button" className="icon-btn danger" disabled={form.items.length === 1} onClick={() => setForm((current) => ({ ...current, items: current.items.filter((_, i) => i !== index) }))} title="Remove item">
                    <Trash2 size={17} />
                  </button>
                </div>
              ))}
            </div>
          </section>
        </div>

        <aside className="form-side">
          <section className="card summary-card">
            <h3>Order summary</h3>
            <div className="summary-line"><span>Items</span><strong>{form.items.filter((i) => i.name.trim()).length}</strong></div>
            <div className="summary-line"><span>Quantity</span><strong>{form.items.reduce((sum, i) => sum + (Number(i.quantity) || 0), 0)}</strong></div>
            <div className="summary-total"><span>Estimated total</span><strong>{money(total)}</strong></div>
            <button type="submit" className="btn btn-primary btn-block btn-lg" disabled={busy}>
              {busy ? 'Creating order…' : 'Create order & QR'}
            </button>
            <p className="summary-note"><Info size={14} /> A unique QR receipt is generated instantly after creation.</p>
          </section>
        </aside>
      </form>
    </div>
  );
}
