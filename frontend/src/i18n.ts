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
      errors: {
        // General errors (00XX)
        '1': 'Chyba validace', // Numeric fallback for ErrorCodes.ValidationError = 1
        '2': 'Povinné pole chybí',
        '3': 'Nesprávný formát',
        '4': 'Neplatná hodnota',
        '5': 'Neplatný rozsah dat',
        '6': 'Zdroj nenalezen',
        '7': 'Porušení obchodního pravidla',
        '8': 'Neplatná operace',
        '9': 'Duplicitní záznam',
        '10': 'Interní chyba serveru',
        '11': 'Chyba databáze',
        '12': 'Chyba konfigurace',
        '13': 'Neautorizovaný přístup',
        '14': 'Přístup zakázán',
        '15': 'Token vypršel',
        
        // Purchase module errors (11XX)
        '1101': 'Objednávka nenalezena (ID: {id})',
        '1102': 'Dodavatel nenalezen (ID: {id})',
        '1103': 'Změna stavu není povolena',
        '1104': 'Nedostatečné množství na skladě',
        
        // Catalog module errors (13XX)
        '1301': 'Položka katalogu nenalezena (ID: {id})',
        
        // Transport module errors (14XX)
        '1401': 'Přepravní box nenalezen (ID: {id})',
        
        // Configuration module errors (15XX)
        '1501': 'Konfigurace nenalezena (ID: {id})',
        
        // External Service errors (90XX)
        '9001': 'Chyba externí služby',
        '9002': 'Chyba ABRA Flexi API',
        '9003': 'Chyba Shoptet API',
        '9004': 'Chyba platební brány',
        
        // Legacy error codes for backward compatibility (temporarily keep)
        '1000': 'Chyba validace',
        '2001': 'Objednávka nenalezena (ID: {id})',
        '3001': 'Neplatná operace',
        '5000': 'Interní chyba serveru',
        '6000': 'Neautorizovaný přístup',
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