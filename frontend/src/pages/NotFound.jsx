import { Link } from 'react-router-dom';
import { SearchX, ArrowLeft } from 'lucide-react';

export default function NotFound() {
  return (
    <div className="notfound-page">
      <div className="card notfound-card">
        <SearchX size={38} />
        <h1>404 — Page not found</h1>
        <p>That page went missing, just like an old paper token.</p>
        <Link to="/" className="btn btn-primary"><ArrowLeft size={16} /> Back home</Link>
      </div>
    </div>
  );
}
