import { useCallback, useEffect, useState } from 'react';
import { Users, UserPlus, Shield, UserRound, Trash2, CheckCircle2, CircleSlash } from 'lucide-react';
import api from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import Modal from '../components/Modal';
import EmptyState from '../components/EmptyState';
import Loader from '../components/Loader';
import { initials, formatDate } from '../utils/helpers';

const emptyForm = { name: '', email: '', phone: '', password: '' };

export default function Team() {
  const [members, setMembers] = useState([]);
  const [form, setForm] = useState(emptyForm);
  const [editing, setEditing] = useState(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const toast = useToast();
  const { user } = useAuth();
  const isOwner = user?.role === 'Owner';

  const load = useCallback(() => {
    api.get('/staff').then(({ data }) => { setMembers(data); setLoading(false); })
      .catch((error) => { toast.error(error.message); setLoading(false); });
  }, [toast]);

  useEffect(() => { load(); }, [load]);

  const invite = async (event) => {
    event.preventDefault();
    setBusy(true);
    try {
      await api.post('/staff', form);
      toast.success(`${form.name} can now log in as staff.`);
      setForm(emptyForm);
      load();
    } catch (error) { toast.error(error.message); }
    finally { setBusy(false); }
  };

  const saveEdit = async (event) => {
    event.preventDefault();
    setBusy(true);
    try {
      await api.put(`/staff/${editing.id}`, { name: editing.name, phone: editing.phone, isActive: editing.isActive });
      toast.success('Team member updated.');
      setEditing(null);
      load();
    } catch (error) { toast.error(error.message); }
    finally { setBusy(false); }
  };

  const deactivate = async (member) => {
    if (!window.confirm(`Deactivate ${member.name}'s account?`)) return;
    try {
      await api.delete(`/staff/${member.id}`);
      toast.success('Account deactivated.');
      load();
    } catch (error) { toast.error(error.message); }
  };

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div><h2>Your team</h2><p>Staff accounts can manage orders and record payments.</p></div>
      </div>

      <div className="team-grid">
        <section className="card">
          <div className="card-head"><div><h3>Team members</h3><p>{members.length} account(s) in this shop</p></div></div>
          {loading ? <Loader full label="Loading team…" /> : members.length === 0 ? (
            <EmptyState icon={Users} title="No team yet" description="Invite a staff member to help at the counter." />
          ) : (
            <div className="team-list">
              {members.map((member) => (
                <div className="team-row" key={member.id}>
                  <span className="avatar">{initials(member.name)}</span>
                  <div className="team-info">
                    <strong>{member.name}</strong>
                    <span>{member.email}</span>
                  </div>
                  <span className={`soft-chip ${member.role === 'Owner' ? 'owner-chip' : ''}`}>{member.role}</span>
                  {member.isActive
                    ? <span className="badge payment-Paid"><i className="badge-dot" /> Active</span>
                    : <span className="badge payment-Unpaid"><i className="badge-dot" /> Inactive</span>}
                  <span className="muted team-date">{formatDate(member.createdAt)}</span>
                  {member.role === 'Owner' ? (
                    <span className="team-owner-note"><Shield size={15} /> Owner</span>
                  ) : isOwner ? (
                    <div className="team-actions">
                      <button className="btn btn-ghost btn-sm" onClick={() => setEditing({ ...member })}>Edit</button>
                      <button className="icon-btn danger" onClick={() => deactivate(member)} title="Deactivate"><Trash2 size={16} /></button>
                    </div>
                  ) : null}
                </div>
              ))}
            </div>
          )}
        </section>

        {isOwner && <aside className="card invite-card">
          <div className="section-title">
            <span className="section-icon"><UserPlus size={19} /></span>
            <div><h3>Invite staff</h3><p>They get their own login immediately.</p></div>
          </div>
          <form onSubmit={invite} className="form-stack">
            <label className="field"><span>Full name</span><div className="input-with-icon"><UserRound size={17} /><input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Staff name" required /></div></label>
            <label className="field"><span>Email</span><input type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} placeholder="staff@shop.com" required /></label>
            <label className="field"><span>Phone</span><input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} placeholder="+880 17xx-xxxxxx" /></label>
            <label className="field"><span>Temporary password</span><input type="password" minLength={6} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} placeholder="At least 6 characters" required /></label>
            <button className="btn btn-primary btn-block" disabled={busy}><UserPlus size={16} /> {busy ? 'Inviting…' : 'Invite staff'}</button>
            <p className="tiny-note">Share the credentials securely. Staff cannot edit shop settings, billing or cancel orders.</p>
          </form>
        </aside>}
      </div>

      <Modal
        open={!!editing}
        onClose={() => setEditing(null)}
        title="Edit team member"
        footer={<><button className="btn btn-ghost btn-sm" onClick={() => setEditing(null)}>Cancel</button><button form="edit-staff-form" type="submit" className="btn btn-primary btn-sm" disabled={busy}>Save</button></>}
      >
        {editing && (
          <form id="edit-staff-form" onSubmit={saveEdit} className="form-stack">
            <label className="field"><span>Name</span><input value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} required /></label>
            <label className="field"><span>Phone</span><input value={editing.phone} onChange={(e) => setEditing({ ...editing, phone: e.target.value })} /></label>
            <label className="field toggle-field">
              <span>Account active</span>
              <button type="button" className={`toggle ${editing.isActive ? 'on' : ''}`} onClick={() => setEditing({ ...editing, isActive: !editing.isActive })} aria-label="Toggle active">
                <span />
              </button>
            </label>
          </form>
        )}
      </Modal>
    </div>
  );
}
