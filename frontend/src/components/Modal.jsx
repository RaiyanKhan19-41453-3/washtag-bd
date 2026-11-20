import { useEffect, useRef } from 'react';
import { X } from 'lucide-react';

export default function Modal({ open, onClose, title, subtitle, children, footer, size = 'md' }) {
  const dialogRef = useRef(null);
  const previousFocus = useRef(null);
  // Callers pass a fresh inline arrow each render; depending on it would refocus
  // the dialog on every keystroke and steal focus from modal inputs.
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    if (!open) return undefined;
    previousFocus.current = document.activeElement;
    // Focus the first form control so it is ready for typing (e.g. the payment
    // amount field). React's autoFocus prop is consumed before this effect runs,
    // so it cannot be detected here and would otherwise be blurred by the
    // dialog-container focus below.
    const preferred = dialogRef.current?.querySelector(
      'input:not([type=hidden]):not([disabled]), textarea:not([disabled]), select:not([disabled])');
    (preferred ?? dialogRef.current)?.focus();
    const handleKey = (event) => {
      if (event.key === 'Escape') { onCloseRef.current?.(); return; }
      if (event.key !== 'Tab') return;
      // Keep keyboard focus inside the dialog until it closes.
      const focusable = dialogRef.current?.querySelectorAll(
        'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])');
      if (!focusable?.length) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;
      // Focus on the dialog container (or one that escaped behind the modal)
      // must be pulled back inside — otherwise Shift+Tab roams the background.
      if (active === dialogRef.current || !dialogRef.current?.contains(active)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
        return;
      }
      if (event.shiftKey && active === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && active === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('keydown', handleKey);
      previousFocus.current?.focus?.();
    };
  }, [open]);

  if (!open) return null;
  return (
    <div className="modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose?.(); }}>
      <div
        ref={dialogRef}
        tabIndex={-1}
        className={`modal modal-${size}`}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <div className="modal-head">
          <div>
            <h3>{title}</h3>
            {subtitle && <p>{subtitle}</p>}
          </div>
          <button className="icon-btn" onClick={onClose} aria-label="Close"><X size={19} /></button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-foot">{footer}</div>}
      </div>
    </div>
  );
}
