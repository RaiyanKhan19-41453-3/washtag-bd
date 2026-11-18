import { STATUS_LABELS, formatDateTime } from '../utils/helpers';
import { useLanguage } from '../context/LanguageContext';

export default function Timeline({ events = [] }) {
  const { t } = useLanguage();
  if (!events.length) return <p className="muted">No activity yet.</p>;

  return (
    <div className="timeline">
      {events.map((event, index) => (
        <div key={event.id || index} className={`timeline-item ${index === events.length - 1 ? 'is-current' : ''}`}>
          <div className="timeline-marker" />
          <div className="timeline-content">
            <div className="timeline-top">
              <strong>{t(STATUS_LABELS[event.toStatus] || event.toStatus)}</strong>
              <time>{formatDateTime(event.createdAt)}</time>
            </div>
            {event.note && <p>{event.note}</p>}
            <span className="timeline-actor">{t('by')} {event.actorName}</span>
          </div>
        </div>
      ))}
    </div>
  );
}
