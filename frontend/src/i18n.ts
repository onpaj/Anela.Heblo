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
        // String keys for ErrorCodes enum names (primary approach)
        // General errors
        'ValidationError': 'Chyba validace',
        'RequiredFieldMissing': 'Povinné pole chybí',
        'InvalidFormat': 'Nesprávný formát',
        'InvalidValue': 'Neplatná hodnota',
        'InvalidDateRange': 'Neplatný rozsah dat',
        'ResourceNotFound': 'Zdroj nenalezen',
        'BusinessRuleViolation': 'Porušení obchodního pravidla',
        'InvalidOperation': 'Neplatná operace',
        'DuplicateEntry': 'Duplicitní záznam',
        'InternalServerError': 'Interní chyba serveru',
        'DatabaseError': 'Chyba databáze',
        'ConfigurationError': 'Chyba konfigurace',
        'Unauthorized': 'Neautorizovaný přístup',
        'Forbidden': 'Přístup zakázán',
        'TokenExpired': 'Token vypršel',
        'Exception': 'Výjimka aplikace',
        
        // Purchase module errors
        'PurchaseOrderNotFound': 'Objednávka nenalezena (ID: {id})',
        'SupplierNotFound': 'Dodavatel nenalezen (ID: {id})',
        'StatusTransitionNotAllowed': 'Změna stavu není povolena',
        'InsufficientStock': 'Nedostatečné množství na skladě',
        
        // Catalog module errors
        'CatalogItemNotFound': 'Položka katalogu nenalezena (ID: {id})',
        'ManufactureDifficultyNotFound': 'Nastavení obtížnosti výroby nenalezeno (ID: {id})',
        'ManufactureDifficultyConflict': 'Konflikt při ukládání obtížnosti výroby',
        'MarginCalculationError': 'Chyba při výpočtu marží',
        'DataAccessUnavailable': 'Zdroj dat není dostupný',
        
        // Transport module errors
        'TransportBoxNotFound': 'Přepravní box nenalezen (ID: {id})',
        'TransportBoxStateChangeError': 'Chyba při změně stavu přepravního boxu',
        'TransportBoxCreationError': 'Chyba při vytváření přepravního boxu',
        'TransportBoxItemError': 'Chyba při práci s položkami v přepravním boxu',
        'TransportBoxDuplicateActiveBoxFound':'Box s číslem {code} již existuje a je stále aktivní',
        
        // Configuration module errors
        'ConfigurationNotFound': 'Konfigurace nenalezena (ID: {id})',
        
        // External Service errors
        'ExternalServiceError': 'Chyba externí služby',
        'FlexiApiError': 'Chyba ABRA Flexi API',
        'ShoptetApiError': 'Chyba Shoptet API',
        'PaymentGatewayError': 'Chyba platební brány',
        
        // Numeric keys as fallback (for backward compatibility)
        // General errors (00XX)
        '1': 'Chyba validace',
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
        '99': 'Výjimka aplikace',
        
        // Purchase module errors (11XX)
        '1101': 'Objednávka nenalezena (ID: {id})',
        '1102': 'Dodavatel nenalezen (ID: {id})',
        '1103': 'Změna stavu není povolena',
        '1104': 'Nedostatečné množství na skladě',
        
        // Catalog module errors (13XX)
        '1301': 'Položka katalogu nenalezena (ID: {id})',
        '1302': 'Nastavení obtížnosti výroby nenalezeno (ID: {id})',
        '1303': 'Konflikt při ukládání obtížnosti výroby',
        '1304': 'Chyba při výpočtu marží',
        '1305': 'Zdroj dat není dostupný',
        
        // Transport module errors (14XX)
        '1401': 'Přepravní box nenalezen (ID: {id})',
        '1402': 'Chyba při změně stavu přepravního boxu',
        '1403': 'Chyba při vytváření přepravního boxu',
        '1404': 'Chyba při práci s položkami v přepravním boxu',
        
        // Configuration module errors (15XX)
        '1501': 'Konfigurace nenalezena (ID: {id})',
        
        // External Service errors (90XX)
        '9001': 'Chyba externí služby',
        '9002': 'Chyba ABRA Flexi API',
        '9003': 'Chyba Shoptet API',
        '9004': 'Chyba platební brány',
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