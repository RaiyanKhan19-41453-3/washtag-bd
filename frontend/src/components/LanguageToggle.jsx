import { Languages } from 'lucide-react';
import { useLanguage } from '../context/LanguageContext';

export default function LanguageToggle({ compact = false }) {
  const { lang, toggle } = useLanguage();
  return (
    <button
      type="button"
      className={`lang-toggle ${compact ? 'lang-toggle-compact' : ''}`}
      onClick={toggle}
      title={lang === 'bn' ? 'Switch to English' : 'বাংলায় পরিবর্তন করুন'}
      aria-label="Toggle language"
    >
      <Languages size={15} />
      <span>{lang === 'bn' ? 'বাং' : 'EN'}</span>
    </button>
  );
}
