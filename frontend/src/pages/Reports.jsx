import { useEffect, useState } from 'react';
import { Download, FileSpreadsheet } from 'lucide-react';
import api from '../api/client';
import { useToast } from '../context/ToastContext';
import { useLanguage } from '../context/LanguageContext';
import Loader from '../components/Loader';
import { money, formatDate } from '../utils/helpers';

const today = new Date();
const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);
const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
// Business days are UTC+6 (Dhaka); send the day boundaries as UTC instants.
const dayStartUtc = (day) => new Date(`${day}T00:00:00+06:00`).toISOString();
// "To" is an inclusive calendar day; the API uses an exclusive upper bound.
const exclusiveEndUtc = (day) => {
  const d = new Date(`${day}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + 1);
  return dayStartUtc(d.toISOString().slice(0, 10));
};

export default function Reports() {
  const toast = useToast();
  const { t } = useLanguage();
  const [from, setFrom] = useState(iso(firstOfMonth));
  const [to, setTo] = useState(iso(new Date(today.getTime() + 86400000)));
  const [report, setReport] = useState(null);
  const [busy, setBusy] = useState(false);

  const load = async () => {
    setBusy(true);
    try {
      const { data } = await api.get('/reports/daily', {
        params: { from: dayStartUtc(from), to: exclusiveEndUtc(to) }
      });
      setReport(data);
    } catch (error) { toast.error(error.message); }
    finally { setBusy(false); }
  };
  useEffect(() => { load(); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const download = async () => {
    try {
      const response = await api.get('/reports/orders.csv', {
        params: { from: dayStartUtc(from), to: exclusiveEndUtc(to) },
        responseType: 'blob'
      });
      const url = URL.createObjectURL(new Blob([response.data], { type: 'text/csv' }));
      const link = document.createElement('a');
      link.href = url;
      link.download = `orders-${from}-${to}.csv`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (error) { toast.error(error.message); }
  };

  if (!report && busy) return <Loader full label="Building report…" />;

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div><h2>{t('Daily register')}</h2><p>{t('Orders and collections for a date range')}</p></div>
        <button className="btn btn-outline btn-sm" onClick={download}><Download size={15} /> {t('Export CSV')}</button>
      </div>
      <section className="card">
        <div className="form-row">
          <label className="field"><span>{t('From')}</span><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
          <label className="field"><span>{t('To')}</span><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
          <button className="btn btn-primary" onClick={load} disabled={busy}>{t('Refresh')}</button>
        </div>
      </section>
      {report && (
        <>
          <div className="stat-grid">
            <div className="stat-card"><span>{t('Orders')}</span><strong>{report.orderCount}</strong></div>
            <div className="stat-card"><span>{t('Collected')}</span><strong>{money(report.collected)}</strong></div>
            <div className="stat-card"><span>{t('Outstanding')}</span><strong>{money(report.outstanding)}</strong></div>
          </div>
          <section className="card">
            <div className="card-head"><div><h3>{t('By payment method')}</h3></div></div>
            <div className="table-scroll"><table className="data-table">
              <thead><tr><th>{t('Method')}</th><th className="align-right">{t('Amount')}</th></tr></thead>
              <tbody>{report.byMethod.map((m) => <tr key={m.method}><td>{m.method}</td><td className="align-right">{money(m.amount)}</td></tr>)}</tbody>
            </table></div>
          </section>
          <section className="card">
            <div className="card-head"><div><h3>{t('By day')}</h3></div><FileSpreadsheet size={18} /></div>
            <div className="table-scroll"><table className="data-table">
              <thead><tr><th>{t('Date')}</th><th className="align-right">{t('Orders')}</th><th className="align-right">{t('Collected')}</th></tr></thead>
              <tbody>{report.byDay.map((d) => <tr key={d.date}><td>{String(d.date).slice(0, 10)}</td><td className="align-right">{d.orders}</td><td className="align-right">{money(d.collected)}</td></tr>)}</tbody>
            </table></div>
          </section>
        </>
      )}
    </div>
  );
}
