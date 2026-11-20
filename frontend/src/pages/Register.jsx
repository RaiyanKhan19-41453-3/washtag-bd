import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Store, UserRound, Mail, Phone, Lock, MapPin, ArrowRight, Sparkles } from 'lucide-react';
import LogoMark from '../components/LogoMark';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';

export default function Register() {
  const [form, setForm] = useState({
    shopName: '', ownerName: '', email: '', phone: '', password: '', address: '', city: 'Dhaka'
  });
  const [busy, setBusy] = useState(false);
  const { register } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();

  const update = (key) => (event) => setForm((current) => ({ ...current, [key]: event.target.value }));

  const submit = async (event) => {
    event.preventDefault();
    setBusy(true);
    try {
      await register(form);
      toast.success('Your shop is ready. Welcome to WashTag BD!');
      navigate('/app', { replace: true });
    } catch (error) {
      toast.error(error.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-hero auth-hero-register">
        <Link to="/" className="brand brand-light">
          <div className="brand-mark"><LogoMark size={21} /></div>
          <div className="brand-text"><strong>WashTag<span>BD</span></strong><small>Smart Laundry OS</small></div>
        </Link>
        <div className="auth-hero-copy">
          <span className="eyebrow"><Sparkles size={14} /> Get started in minutes</span>
          <h1>Put your shop on the map.</h1>
          <p>Create your workspace, add your first order, and hand your customers a QR they'll actually keep.</p>
          <div className="auth-hero-points">
            <span>✓ Free to try, ৳500/mo after</span>
            <span>✓ No hardware required</span>
            <span>✓ Cancel anytime</span>
          </div>
        </div>
        <p className="auth-hero-foot">Dhanmondi · Gulshan · Uttara · Chittagong & beyond.</p>
      </div>

      <div className="auth-panel">
        <div className="auth-form-wrap auth-form-wide">
          <div className="auth-form-head">
            <h2>Create your shop</h2>
            <p>A few details and you're live.</p>
          </div>

          <form onSubmit={submit} className="form-stack">
            <div className="form-row">
              <label className="field">
                <span>Shop name</span>
                <div className="input-with-icon"><Store size={17} /><input value={form.shopName} onChange={update('shopName')} placeholder="e.g. Shine & Fold Laundry" required /></div>
              </label>
              <label className="field">
                <span>Owner name</span>
                <div className="input-with-icon"><UserRound size={17} /><input value={form.ownerName} onChange={update('ownerName')} placeholder="Your full name" required /></div>
              </label>
            </div>

            <div className="form-row">
              <label className="field">
                <span>Email address</span>
                <div className="input-with-icon"><Mail size={17} /><input type="email" value={form.email} onChange={update('email')} placeholder="you@shop.com" required /></div>
              </label>
              <label className="field">
                <span>Phone number</span>
                <div className="input-with-icon"><Phone size={17} /><input value={form.phone} onChange={update('phone')} placeholder="+880 17xx-xxxxxx" required /></div>
              </label>
            </div>

            <div className="form-row">
              <label className="field">
                <span>Password</span>
                <div className="input-with-icon"><Lock size={17} /><input type="password" value={form.password} onChange={update('password')} placeholder="At least 6 characters" minLength={6} required /></div>
              </label>
              <label className="field">
                <span>City</span>
                <div className="input-with-icon"><MapPin size={17} /><input value={form.city} onChange={update('city')} placeholder="Dhaka" required /></div>
              </label>
            </div>

            <label className="field">
              <span>Shop address <em>(optional)</em></span>
              <div className="input-with-icon"><MapPin size={17} /><input value={form.address} onChange={update('address')} placeholder="Road 7, Dhanmondi" /></div>
            </label>

            <button type="submit" className="btn btn-primary btn-block btn-lg" disabled={busy}>
              {busy ? 'Creating your shop…' : <>Create workspace <ArrowRight size={18} /></>}
            </button>
          </form>

          <p className="auth-switch">
            Already have an account? <Link to="/login">Log in</Link>
          </p>
          <p className="auth-back"><Link to="/">← Back to home</Link></p>
        </div>
      </div>
    </div>
  );
}
