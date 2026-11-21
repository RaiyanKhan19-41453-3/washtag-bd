import { Link } from 'react-router-dom';
import {
  QrCode, ArrowRight, ArrowUpRight, Check, MapPin,
  ReceiptText, Smartphone, ScanLine, BarChart3
} from 'lucide-react';
import QRCode from '../components/QRCode';
import LogoMark from '../components/LogoMark';
import { STATUS_ORDER, STATUS_LABELS, trackingUrl } from '../utils/helpers';

const features = [
  {
    title: 'One QR at drop-off. Nothing to lose.',
    text: 'Print or show a single QR receipt. No faded paper tokens, no torn slips, no "which ticket was mine?".',
    Icon: QrCode
  },
  {
    title: 'Customers just point their camera.',
    text: 'No app, no sign-up, no explanation. The receipt opens a live page they can check from anywhere.',
    Icon: Smartphone
  },
  {
    title: 'Every step is visible, before they ask.',
    text: 'Received → Washing → Ironing → Ready → Delivered. The counter stops being a black box.',
    Icon: ScanLine
  },
  {
    title: 'Your counter, in numbers.',
    text: 'Pending items, unpaid customers, daily collections, without opening a khata or a spreadsheet.',
    Icon: BarChart3
  }
];

const steps = [
  { n: '01', title: 'Drop-off', text: 'Create the order, hand over one QR receipt.' },
  { n: '02', title: 'In the shop', text: 'Scan it at each stage to move the order forward.' },
  { n: '03', title: 'On the customer phone', text: 'Live status and payment, always up to date.' }
];

const plans = [
  { name: 'Starter', price: 500, desc: 'A single counter finding its feet.', perks: ['1 shop · 2 staff', 'Unlimited orders', 'QR tracking & payments'] },
  { name: 'Growth', price: 800, desc: 'For a steady, busy flow.', perks: ['Up to 5 staff', 'Customer directory', 'Realtime updates'], featured: true },
  { name: 'Pro', price: 1000, desc: 'More branches, more hands.', perks: ['Unlimited staff', 'Priority support', 'Custom branding'] }
];

export default function Landing() {
  const demoToken = '00000000-0000-0000-0000-000000000000';

  return (
    <div className="landing">
      <header className="landing-nav">
        <Link to="/" className="brand">
          <div className="brand-mark"><LogoMark /></div>
          <div className="brand-text">
            <strong>WashTag<span>BD</span></strong>
            <small>Smart Laundry OS</small>
          </div>
        </Link>
        <nav>
          <a href="#how">How it works</a>
          <a href="#features">Features</a>
          <a href="#pricing">Pricing</a>
          <Link to="/login" className="landing-login">Log in</Link>
          <Link to="/register" className="btn btn-primary btn-sm">Start free <ArrowRight size={15} /></Link>
        </nav>
      </header>

      {/* Asymmetric hero: oversized type on the left, a real ticket on the right. */}
      <section className="hero">
        <div className="hero-copy">
          <div className="hero-index"><span>01 / SMART LAUNDRY OS</span><span>MADE IN DHAKA</span></div>
          <h1>Paper tokens<br /><em>are finished.</em></h1>
          <p className="hero-lead">
            Every order becomes a QR receipt your customer can track from their own phone.
            No app to install, no tickets to lose, no arguments at pickup.
          </p>
          <div className="hero-actions">
            <Link to="/register" className="btn btn-primary btn-lg">Set up your shop <ArrowRight size={18} /></Link>
            <Link to="/login" className="btn btn-outline btn-lg">See the demo</Link>
          </div>
          <div className="hero-proof">
            <span><Check size={14} /> No app download</span>
            <span><Check size={14} /> Works on any phone</span>
            <span><Check size={14} /> BDT pricing</span>
          </div>
        </div>

        <div className="hero-visual">
          <div className="ticket">
            <div className="ticket-top">
              <div className="ticket-shop">
                <span className="ticket-shop-icon"><ReceiptText size={17} /></span>
                <div><strong>Shine &amp; Fold Laundry</strong><small><MapPin size={10} /> Dhanmondi, Dhaka</small></div>
              </div>
              <span className="ticket-no">#WT-00042</span>
            </div>
            <div className="ticket-qr">
              <QRCode value={trackingUrl(demoToken)} size={148} />
              <span>Scan to track this order</span>
            </div>
            <div className="ticket-perf" aria-hidden="true" />
            <div className="ticket-status">
              <span className="ticket-label">CURRENT STATUS</span>
              <strong>Ironing</strong>
              <div className="ticket-steps">
                {STATUS_ORDER.map((status, i) => (
                  <div key={status} className={`ticket-step ${i <= 2 ? 'is-done' : ''}`}>
                    <span className="ticket-dot" />
                    <small>{STATUS_LABELS[status]}</small>
                  </div>
                ))}
              </div>
            </div>
          </div>
          <div className="hero-facts">
            <div><strong>5</strong><span>steps, visible to the customer</span></div>
            <div><strong>0</strong><span>apps to install</span></div>
          </div>
        </div>
      </section>

      {/* Numbered, ruled rows instead of a grid of icon tiles. */}
      <section id="how" className="landing-section">
        <div className="section-label"><span>02</span> THE FLOW</div>
        <div className="section-lead">
          <h2>Three steps. No friction.</h2>
          <p>From the counter to the customer's pocket, in under a minute.</p>
        </div>
        <div className="step-list">
          {steps.map((step) => (
            <div key={step.n} className="step-row">
              <span className="step-n">{step.n}</span>
              <h3>{step.title}</h3>
              <p>{step.text}</p>
            </div>
          ))}
        </div>
      </section>

      <section id="features" className="landing-section features-section">
        <div className="section-label"><span>03</span> WHY SHOPS SWITCH</div>
        <div className="section-lead">
          <h2>Less arguing. More flow.</h2>
          <p>Everything a modern counter needs, without enterprise complexity.</p>
        </div>
        <div className="feature-list">
          {features.map(({ title, text, Icon }, i) => (
            <div key={title} className="feature-row">
              <span className="feature-no">0{i + 1}</span>
              <Icon size={22} className="feature-icon" />
              <div>
                <h3>{title}</h3>
                <p>{text}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* Pricing as a ruled comparison table; the featured row is a solid block. */}
      <section id="pricing" className="landing-section pricing-section">
        <div className="section-label"><span>04</span> PRICING</div>
        <div className="section-lead">
          <h2>Less than a few coffees a month.</h2>
          <p>Monthly plans in BDT. Cancel anytime. No setup fee, no hardware lock-in.</p>
        </div>
        <div className="price-table">
          <div className="price-head">
            <span>PLAN</span><span>WHO IT'S FOR</span><span>PER MONTH</span><span />
          </div>
          {plans.map((plan) => (
            <div key={plan.name} className={`price-row ${plan.featured ? 'is-featured' : ''}`}>
              <div className="price-name">
                <strong>{plan.name}</strong>
                {plan.featured && <span className="price-flag">MOST POPULAR</span>}
              </div>
              <span className="price-desc">{plan.desc}</span>
              <div className="price-amount"><strong>৳{plan.price}</strong><small>/month</small></div>
              <div className="price-perks">
                {plan.perks.map((perk) => <span key={perk}><Check size={13} /> {perk}</span>)}
              </div>
              <Link to="/register" className={`btn btn-sm ${plan.featured ? 'btn-light' : 'btn-outline'}`}>
                Start {plan.name} <ArrowUpRight size={14} />
              </Link>
            </div>
          ))}
        </div>
      </section>

      <section className="cta-band">
        <div className="cta-inner">
          <div>
            <span className="cta-kicker">READY WHEN YOU ARE</span>
            <h2>Your counter deserves better than paper.</h2>
            <p>Set up in minutes. No hardware, no contract, no looking back.</p>
          </div>
          <Link to="/register" className="btn btn-light btn-lg">Open your workspace <ArrowRight size={18} /></Link>
        </div>
      </section>

      <footer className="landing-footer">
        <div className="brand">
          <div className="brand-mark"><LogoMark /></div>
          <strong>WashTag<span>BD</span></strong>
        </div>
        <p>© {new Date().getFullYear()} WashTag BD · Built for the neighbourhood shops of Bangladesh.</p>
      </footer>
    </div>
  );
}
