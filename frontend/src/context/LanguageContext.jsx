import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { translate } from '../i18n/translations';

const LanguageContext = createContext(null);

export function LanguageProvider({ children }) {
  const [lang, setLang] = useState(() => localStorage.getItem('washtag_lang') || 'en');

  useEffect(() => {
    localStorage.setItem('washtag_lang', lang);
    document.documentElement.lang = lang === 'bn' ? 'bn' : 'en';
  }, [lang]);

  const t = useCallback((text) => translate(lang, text), [lang]);
  const toggle = useCallback(() => setLang((current) => (current === 'bn' ? 'en' : 'bn')), []);

  const value = useMemo(() => ({ lang, setLang, toggle, t, isBengali: lang === 'bn' }), [lang, t, toggle]);
  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useLanguage() {
  const context = useContext(LanguageContext);
  if (!context) throw new Error('useLanguage must be used inside LanguageProvider');
  return context;
}
