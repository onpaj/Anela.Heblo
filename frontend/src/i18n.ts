import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';

const resources = {
  cs: {
    translation: {
      navigation: {
        catalog: 'Katalog',
        manufacture: 'Výroba',
        purchase: 'Nákup',
        transport: 'Doprava',
        invoices: 'Faktury',
      },
      common: {
        openMenu: 'Otevřít menu',
        search: 'Hledat...',
        loading: 'Načítá se...',
        welcome: 'Vítejte v Anela Heblo',
        appLoading: 'Aplikace se načítá...',
      },
    },
  },
  en: {
    translation: {
      navigation: {
        catalog: 'Catalog',
        manufacture: 'Manufacture',
        purchase: 'Purchase',
        transport: 'Transport',
        invoices: 'Invoices',
      },
      common: {
        openMenu: 'Open menu',
        search: 'Search...',
        loading: 'Loading...',
        welcome: 'Welcome to Anela Heblo',
        appLoading: 'Application is loading...',
      },
    },
  },
};

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: 'cs', // Czech as primary language per design doc
    debug: process.env.NODE_ENV === 'development',
    interpolation: {
      escapeValue: false,
    },
    detection: {
      order: ['localStorage', 'navigator', 'htmlTag'],
      caches: ['localStorage'],
    },
  });

export default i18n;