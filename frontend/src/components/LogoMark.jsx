// WashTag BD brand mark - bare line art, no tile or fill. A price tag whose
// punch-hole reads as a water drop, with a bold "W" in the body. Every part is
// a stroke in currentColor, so the mark inherits the surrounding brand colour.
export default function LogoMark({ size = 26, className }) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {/* tag outline - pointed at bottom-left, flat shoulder at top-right */}
      <path d="M13.4 2.6H19a2.4 2.4 0 0 1 2.4 2.4v5.6a2.4 2.4 0 0 1-.7 1.7L11 22.4a2.4 2.4 0 0 1-3.4 0L2 16.8a2.4 2.4 0 0 1 0-3.4L11.7 3.3a2.4 2.4 0 0 1 1.7-.7Z" />
      {/* punch-hole / drop */}
      <circle cx="17.1" cy="7.1" r="1.35" />
      {/* W in the tag body */}
      <path d="m7.4 11.6 1.9 4.1 2.1-4.1 2.1 4.1 1.9-4.1" strokeWidth="1.9" />
    </svg>
  );
}
