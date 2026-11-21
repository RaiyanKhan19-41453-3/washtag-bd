import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';
import { Store, MapPin, Phone, Image as ImageIcon, Save, ShieldCheck, BadgeCheck, Mail, Info, KeyRound } from 'lucide-react';
import api from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import Loader from '../components/Loader';
import { formatDate, shopLocked } from '../utils/helpers';

export default function Settings() {
  const { user, shop, setShop, logout } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const { t } = useLanguage();
  const [form, setForm] = useState(null);
  const [loadError, setLoadError] = useState('');
  const [busy, setBusy] = useState(false);
  const [passwords, setPasswords] = useState({ currentPassword: '', newPassword: '' });
  const [services, setServices] = useState([]);
  const [newService, setNewService] = useState({ name: '', defaultPrice: 120 });

  const loadShop = useCallback(() => {
    setLoadError('');
    api.get('/shop').then(({ data }) => setForm({
      name: data.name || '', address: data.address || '', city: data.city || '',
      phone: data.phone || '', logoUrl: data.logoUrl || ''
    })).catch((error) => { setLoadError(error.message); toast.error(error.message); });
  }, [toast]);

  useEffect(() => { loadShop(); }, [loadShop]);

  const loadServices = useCallback(() => {
    api.get('/services').then(({ data }) => setServices(data)).catch(() => {});
  }, []);
  useEffect(() => { loadServices(); }, [loadServices]);

  const addService = async (event) => {
    event.preventDefault();
    if (!newService.name.trim()) return;
    try {
      await api.post('/services', newService);
      setNewService({ name: '', defaultPrice: 120 });
      loadServices();
      toast.success('Service added.');
    } catch (error) { toast.error(error.message); }
  };

  const updateService = async (service, patch) => {
    try {
      await api.put(`/services/${service.id}`, { name: service.name, defaultPrice: service.defaultPrice,
        isActive: service.isActive, sortOrder: service.sortOrder, ...patch });
      loadServices();
    } catch (error) { toast.error(error.message); }
  };

  const removeService = async (service) => {
    if (!window.confirm(`Remove service "${service.name}"?`)) return;
    try {
      await api.delete(`/services/${service.id}`);
      loadServices();
    } catch (error) { toast.error(error.message); }
  };

  const update = (key) => (event) => setForm((current) => ({ ...current, [key]: event.target.value }));

  const save = async (event) => {
    event.preventDefault();
    setBusy(true);
    try {
      const { data } = await api.put('/shop', form);
      setShop(data);
      toast.success('Shop profile updated.');
    } catch (error) {
      toast.error(error.message);
    } finally {
      setBusy(false);
    }
  };

  const changePassword = async (event) => {
    event.preventDefault();
    setBusy(true);
    try {
      await api.post('/auth/change-password', passwords);
      setPasswords({ currentPassword: '', newPassword: '' });
      // Changing the password invalidates the current token too - sign in again.
      logout();
      navigate('/login', { replace: true, state: { from: '/app/settings' } });
      toast.success('Password changed. Please sign in again with your new password.');
    } catch (error) {
      toast.error(error.message);
    } finally {
      setBusy(false);
    }
  };

  if (loadError) {
    return (
      <div className="card error-card">
        <p>{loadError}</p>
        <button className="btn btn-outline btn-sm" onClick={loadShop}>Retry</button>
      </div>
    );
  }
  if (!form) return <Loader full label="Loading settings…" />;

  return (
    <div className="page-stack settings-page">
      <div className="page-heading">
        <div><h2>Settings</h2><p>Manage how your shop appears on QR receipts.</p></div>
      </div>

      <div className="settings-grid">
        <form onSubmit={save} className="card form-section">
          <div className="section-title">
            <span className="section-icon"><Store size={19} /></span>
            <div><h3>Shop profile</h3><p>This information appears on the customer tracking page.</p></div>
          </div>

          <label className="field">
            <span>Shop name</span>
            <div className="input-with-icon"><Store size={17} /><input value={form.name} onChange={update('name')} required /></div>
          </label>
          <div className="form-row">
            <label className="field">
              <span>City</span>
              <div className="input-with-icon"><MapPin size={17} /><input value={form.city} onChange={update('city')} /></div>
            </label>
            <label className="field">
              <span>Phone</span>
              <div className="input-with-icon"><Phone size={17} /><input value={form.phone} onChange={update('phone')} /></div>
            </label>
          </div>
          <label className="field">
            <span>Address</span>
            <div className="input-with-icon"><MapPin size={17} /><input value={form.address} onChange={update('address')} placeholder="Street, area, landmark…" /></div>
          </label>
          <label className="field">
            <span>Logo URL <em>(optional)</em></span>
            <div className="input-with-icon"><ImageIcon size={17} /><input value={form.logoUrl} onChange={update('logoUrl')} placeholder="https://…/logo.png" /></div>
          </label>

          {user?.role === 'Owner' && (
            <button className="btn btn-primary" type="submit" disabled={busy}><Save size={16} /> {busy ? 'Saving…' : 'Save changes'}</button>
          )}
        </form>

        {user?.role === 'Owner' && (
          <section className="card">
            <div className="card-head"><div><h3>{t('Your services')}</h3><p>{t('Prices autofill when creating an order')}</p></div></div>
            <div className="table-scroll">
              <table className="data-table">
                <thead><tr><th>{t('Service')}</th><th>{t('Default price')}</th><th>{t('Active')}</th><th /></tr></thead>
                <tbody>
                  {services.map((service) => (
                    <tr key={service.id}>
                      <td>{service.name}</td>
                      <td>
                        <input type="number" min="0" defaultValue={service.defaultPrice}
                          onBlur={(e) => Number(e.target.value) !== service.defaultPrice &&
                            updateService(service, { defaultPrice: Number(e.target.value) })} />
                      </td>
                      <td><button className="btn btn-ghost btn-sm" onClick={() => updateService(service, { isActive: !service.isActive })}>{service.isActive ? 'Yes' : 'No'}</button></td>
                      <td><button className="btn btn-ghost btn-sm danger-text" onClick={() => removeService(service)}>{t('Remove')}</button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <form onSubmit={addService} className="form-row" style={{ marginTop: 12 }}>
              <label className="field"><span>{t('New service')}</span><input value={newService.name} onChange={(e) => setNewService({ ...newService, name: e.target.value })} placeholder="e.g. Saree Dry Clean" required /></label>
              <label className="field"><span>{t('Default price')}</span><input type="number" min="0" value={newService.defaultPrice} onChange={(e) => setNewService({ ...newService, defaultPrice: Number(e.target.value) })} /></label>
              <button className="btn btn-primary" type="submit">{t('Add service')}</button>
            </form>
          </section>
        )}

        <aside className="settings-aside">
          <section className="card info-panel">
            <div className="panel-heading"><BadgeCheck size={20} /><h3>Subscription</h3></div>
            <div className="plan-badge">{shop?.plan || 'Starter'} plan</div>
            <p className="muted">{shopLocked(shop) ? 'Access is locked' : 'Renews'} {shop?.subscriptionEndsAt ? formatDate(shop.subscriptionEndsAt) : '-'}</p>
            <div className={`plan-line ${shopLocked(shop) ? 'danger-text' : ''}`}>
              {!shopLocked(shop) && <ShieldCheck size={16} />}
              {shopLocked(shop) ? (shop?.isActive === false ? 'Shop suspended' : `${shop?.subscriptionStatus || 'Inactive'}, renew to continue`) : 'Active & verified'}
            </div>
            <Link to="/app/billing" className="text-link">Manage plan &amp; invoices →</Link>
          </section>

          <section className="card info-panel">
            <div className="panel-heading"><Mail size={20} /><h3>Account</h3></div>
            <div className="info-line"><span>Name</span><strong>{user?.name}</strong></div>
            <div className="info-line"><span>Email</span><strong>{user?.email}</strong></div>
            <div className="info-line"><span>Role</span><strong className="capitalize">{user?.role}</strong></div>
          </section>

          <section className="card info-panel">
            <div className="panel-heading"><KeyRound size={20} /><h3>Change password</h3></div>
            <form onSubmit={changePassword} className="form-stack">
              <label className="field"><span>Current password</span><input type="password" value={passwords.currentPassword} onChange={(e) => setPasswords({ ...passwords, currentPassword: e.target.value })} required /></label>
              <label className="field"><span>New password</span><input type="password" minLength={6} value={passwords.newPassword} onChange={(e) => setPasswords({ ...passwords, newPassword: e.target.value })} required /></label>
              <button className="btn btn-outline btn-sm btn-block" disabled={busy}><KeyRound size={15} /> Update password</button>
            </form>
          </section>

          <section className="card info-panel">
            <div className="panel-heading"><Info size={20} /><h3>How QR tracking works</h3></div>
            <ol className="how-list">
              <li>Create an order and show the QR to the customer.</li>
              <li>They scan it with their phone camera, no app needed.</li>
              <li>You scan the same QR to update status and payments.</li>
            </ol>
          </section>
        </aside>
      </div>
    </div>
  );
}
