import { useEffect, useState } from 'react';
import QRCodeLib from 'qrcode';

export default function QRCode({ value, dataUrl, size = 240, className = '' }) {
  const [src, setSrc] = useState('');

  useEffect(() => {
    if (dataUrl) {
      setSrc(dataUrl);
      return undefined;
    }
    let active = true;
    setSrc('');
    if (value) {
      QRCodeLib.toDataURL(value, {
        width: size * 2,
        margin: 1,
        errorCorrectionLevel: 'M',
        color: { dark: '#0f172a', light: '#ffffff' }
      })
        .then((url) => { if (active) setSrc(url); })
        .catch(() => {});
    }
    return () => { active = false; };
  }, [value, dataUrl, size]);

  return (
    <div className={`qr-wrap ${className}`} style={{ width: size, height: size }}>
      {src ? <img src={src} alt="QR receipt" width={size} height={size} /> : <div className="qr-placeholder" />}
    </div>
  );
}
