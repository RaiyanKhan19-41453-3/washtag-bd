export default function Loader({ label = 'Loading…', full = false }) {
  return (
    <div className={full ? 'loader loader-full' : 'loader loader-inline'}>
      <span className="spinner" />
      {label && <span>{label}</span>}
    </div>
  );
}
