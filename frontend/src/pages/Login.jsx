import { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Mail, Lock, ArrowRight, Eye, EyeOff, Sparkles } from 'lucide-react';
import LogoMark from '../components/LogoMark';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLanguage } from '../context/LanguageContext';
import LanguageToggle from '../components/LanguageToggle';

export default function Login() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);
  const { login } = useAuth();
  const toast = useToast();
  const { t } = useLanguage();
  const navigate = useNavigate();
  const location = useLocation();

  const submit = async (event) => {
    event.preventDefault();
    setBusy(true);
    try {
      await login(email, password);
      toast.success('Welcome back to your workspace!');
      const destination = location.state?.from || sessionStorage.getItem('washtag_from') || '/app';
      sessionStorage.removeItem('washtag_from');
      navigate(destination, { replace: true });
    } catch (error) {
      toast.error(error.message);
    } finally {
      setBusy(false);
    }
  };

  const useDemo = () => {
    setEmail('demo@washtagbd.com');
    setPassword('demo1234');
  };

  return (
    <div className="auth-page">
      <div className="auth-hero">
        <Link to="/" className="brand brand-light">
          <div className="brand-mark"><LogoMark size={21} /></div>
          <div className="brand-text"><strong>WashTag<span>BD</span></strong><small>Smart Laundry OS</small></div>
        </Link>
        <div className="auth-hero-copy">
          <span className="eyebrow"><Sparkles size={14} /> Welcome back</span>
          <h1>Your counter, under control.</h1>
          <p>Pick up right where you left off — pending orders, payments and live QR updates, all in one place.</p>
          <div className="auth-hero-points">
            <span>✓ Live order tracking</span>
            <span>✓ QR scanning built in</span>
            <span>✓ No paper, no disputes</span>
          </div>
        </div>
        <p className="auth-hero-foot">Trusted by neighbourhood shops across Dhaka & Chittagong.</p>
      </div>

      <div className="auth-panel">
        <div className="auth-form-wrap">
          <div className="auth-form-head">
            <div className="login-top-row">
              <div>
                <h2>{t('Log in to your shop')}</h2>
                <p>{t('Enter your account details to continue.')}</p>
              </div>
              <LanguageToggle compact />
            </div>
          </div>

          <form onSubmit={submit} className="form-stack">
            <label className="field">
              <span>{t('Email address')}</span>
              <div className="input-with-icon">
                <Mail size={17} />
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="you@shop.com" required autoComplete="email" />
              </div>
            </label>

            <label className="field">
              <span>{t('Password')}</span>
              <div className="input-with-icon">
                <Lock size={17} />
                <input
                  type={showPassword ? 'text' : 'password'}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••"
                  required
                  autoComplete="current-password"
                />
                <button type="button" className="input-action" onClick={() => setShowPassword((v) => !v)} aria-label="Toggle password">
                  {showPassword ? <EyeOff size={17} /> : <Eye size={17} />}
                </button>
              </div>
            </label>

            <button type="submit" className="btn btn-primary btn-block btn-lg" disabled={busy}>
              {busy ? t('Signing in…') : <>{t('Log in')} <ArrowRight size={18} /></>}
            </button>
          </form>

          <button className="demo-hint" onClick={useDemo} type="button">
            <Sparkles size={15} />
            <span><strong>{t('Try the demo:')}</strong> demo@washtagbd.com / demo1234</span>
          </button>

          <p className="auth-switch">
            New to WashTag BD? <Link to="/register">Create your shop</Link>
          </p>
          <p className="auth-back"><Link to="/">← Back to home</Link></p>
        </div>
      </div>
    </div>
  );
}
