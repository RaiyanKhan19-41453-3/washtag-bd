// WashTag BD brand mark: a price tag containing a bold "W", with the tag's
// punch-hole doubling as a water drop. Single solid colour via currentColor so
// it inherits the teal tile's white foreground everywhere it is used.
export default function LogoMark({ size = 21, className }) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path d="M13.3 3.3 3.3 13.3a2 2 0 0 0 0 2.8l4.6 4.6a2 2 0 0 0 2.8 0l10-10a2 2 0 0 0 0-2.8l-4.6-4.6a2 2 0 0 0-2.8 0Z" />
      <circle cx="7.6" cy="7.6" r="1.25" fill="currentColor" stroke="none" />
      <path d="m8.2 12 1.7 3.8L12 12.6l2.1 3.2 1.7-3.8" strokeWidth="1.9" />
    </svg>
  );
}
