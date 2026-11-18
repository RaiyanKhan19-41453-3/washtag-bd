import { Link } from 'react-router-dom';
import {
  Shirt, QrCode, Smartphone, Clock3, BarChart3, ShieldCheck,
  ArrowRight, CheckCircle2, MapPin, Sparkles
} from 'lucide-react';
import QRCode from '../components/QRCode';
import { STATUS_ORDER, STATUS_LABELS, trackingUrl } from '../utils/helpers';

const features = [
  {
    icon: QrCode,
    title: 'One QR, zero paperwork',
    text: 'Print or show a single QR receipt at drop-off. No more faded tokens or torn slips.'
  },
  {
    icon: Smartphone,
    title: 'Customers scan, no app needed',
    text: 'A phone camera opens a beautiful live status page. Nothing to install, nothing to explain.'
  },
  {
    icon: Clock3,
    title: 'Every step, transparent',
    text: 'Received → Washing → Ironing → Ready → Delivered. Disputes drop before they start.'
  },
  {
    icon: BarChart3,
    title: 'Know your counter at a glance',
    text: 'See pending items, unpaid customers and daily revenue without opening a khata.'
  }
];

const plans = [
  { name: 'Starter', price: 500, desc: 'For a single counter just getting started.', perks: ['1 shop · 2 staff', 'Unlimited orders', 'QR tracking & payments'] },
  { name: 'Growth', price: 800, desc: 'For busy shops with a steady flow.', perks: ['Up to 5 staff', 'Customer directory', 'Realtime updates'], featured: true },
  { name: 'Pro', price: 1000, desc: 'For multi-branch operations.', perks: ['Unlimited staff', 'Priority support', 'Custom branding'] }
];

export default function Landing() {
  const demoToken = '00000000-0000-0000-0000-000000000000';

  return (
    <div className="landing">
      <header className="landing-nav">
        <Link to="/" className="brand">
          <div className="brand-mark"><Shirt size={21} /></div>
          <div className="brand-text">
            <strong>WashTag<span>BD</span></strong>
            <small>Smart Laundry OS</small>
          </div>
        </Link>
        <nav>
          <a href="#features">Features</a>
          <a href="#pricing">Pricing</a>
          <Link to="/login" className="btn btn-ghost btn-sm">Log in</Link>
          <Link to="/register" className="btn btn-primary btn-sm">Start free <ArrowRight size={15} /></Link>
        </nav>
      </header>

      <section className="hero">
        <div className="hero-glow" />
        <div className="hero-copy">
          <span className="eyebrow"><Sparkles size={14} /> Built for Bangladesh</span>
          <h1>Paper tokens? <em>Gone.</em></h1>
          <p className="hero-lead">
            WashTag BD turns every laundry, dry-cleaning and tailor order into a live,
            QR-tracked experience. Your customers always know where their clothes are —
            and you always know what's pending and who hasn't paid.
          </p>
          <div className="hero-actions">
            <Link to="/register" className="btn btn-primary btn-lg">Set up your shop <ArrowRight size={18} /></Link>
            <Link to="/login" className="btn btn-outline btn-lg">See the demo</Link>
          </div>
          <div className="hero-proof">
            <span><CheckCircle2 size={16} /> No app download</span>
            <span><CheckCircle2 size={16} /> Works on any phone</span>
            <span><CheckCircle2 size={16} /> BDT-friendly pricing</span>
          </div>
        </div>

        <div className="hero-visual">
          <div className="phone-mock">
            <div className="phone-notch" />
            <div className="phone-shop">
              <span className="shop-avatar"><Shirt size={18} /></span>
              <div><strong>Shine & Fold Laundry</strong><small><MapPin size={11} /> Dhanmondi, Dhaka</small></div>
            </div>
            <div className="phone-qr">
              <QRCode value={trackingUrl(demoToken)} size={132} />
              <span>Order #WT-00042</span>
            </div>
            <div className="phone-status">
              {STATUS_ORDER.map((status, index) => (
                <div key={status} className={`mini-step ${index <= 2 ? 'done' : ''} ${index === 2 ? 'current' : ''}`}>
                  <span className="mini-dot" />
                  {STATUS_LABELS[status]}
                </div>
              ))}
            </div>
            <div className="phone-pill">Live updates via QR ✨</div>
          </div>
          <div className="floating-card floating-card-a"><ShieldCheck size={18} /> Dispute-free counter</div>
          <div className="floating-card floating-card-b"><CheckCircle2 size={18} /> Paid · ৳450</div>
        </div>
      </section>

      <section id="features" className="landing-section">
        <div className="section-head">
          <span className="eyebrow">WHY SHOPS SWITCH</span>
          <h2>Less arguing. More flow.</h2>
          <p>Everything a modern counter needs, without the complexity of an enterprise system.</p>
        </div>
        <div className="feature-grid">
          {features.map(({ icon: Icon, title, text }) => (
            <div key={title} className="feature-card">
              <div className="feature-icon"><Icon size={22} /></div>
              <h3>{title}</h3>
              <p>{text}</p>
            </div>
          ))}
        </div>
      </section>

      <section id="pricing" className="landing-section pricing-section">
        <div className="section-head">
          <span className="eyebrow">SIMPLE PRICING</span>
          <h2>Less than a few coffees a month</h2>
          <p>Monthly plans in BDT. Cancel anytime. No setup fee, no hardware lock-in.</p>
        </div>
        <div className="pricing-grid">
          {plans.map((plan) => (
            <div key={plan.name} className={`price-card ${plan.featured ? 'featured' : ''}`}>
              {plan.featured && <span className="price-tag">Most popular</span>}
              <h3>{plan.name}</h3>
              <p>{plan.desc}</p>
              <div className="price">৳{plan.price}<small>/month</small></div>
              <ul>
                {plan.perks.map((perk) => <li key={perk}><CheckCircle2 size={16} /> {perk}</li>)}
              </ul>
              <Link to="/register" className={`btn ${plan.featured ? 'btn-primary' : 'btn-outline'} btn-block`}>
                Get started
              </Link>
            </div>
          ))}
        </div>
      </section>

      <section className="cta-band">
        <div>
          <h2>Your counter deserves better than paper.</h2>
          <p>Join the shops making the switch to live, QR-based tracking.</p>
        </div>
        <Link to="/register" className="btn btn-light btn-lg">Open your workspace <ArrowRight size={18} /></Link>
      </section>

      <footer className="landing-footer">
        <div className="brand">
          <div className="brand-mark"><Shirt size={20} /></div>
          <strong>WashTag<span>BD</span></strong>
        </div>
        <p>© {new Date().getFullYear()} WashTag BD. Made for the neighbourhood shops of Bangladesh.</p>
      </footer>
    </div>
  );
}
