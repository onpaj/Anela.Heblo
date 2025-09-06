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
        
        // Manufacture module errors
        'ManufacturingDataNotAvailable': 'Výrobní data nejsou k dispozici pro analýzu',
        'ManufactureAnalysisCalculationFailed': 'Výpočet výrobní analýzy selhal: {reason}',
        'InvalidAnalysisParameters': 'Neplatné parametry analýzy: {parameters}',
        'InsufficientManufacturingData': 'Nedostatečná data pro spolehlivou výrobní analýzu',
        
        // Catalog module errors
        'CatalogItemNotFound': 'Položka katalogu nenalezena (ID: {id})',
        'ManufactureDifficultyNotFound': 'Nastavení obtížnosti výroby nenalezeno (ID: {id})',
        'ManufactureDifficultyConflict': 'Konflikt při ukládání obtížnosti výroby',
        'MarginCalculationError': 'Chyba při výpočtu marží',
        'DataAccessUnavailable': 'Zdroj dat není dostupný',
        'ProductNotFound': 'Produkt s kódem {{productCode}} nebyl nalezen',
        'MaterialNotFound': 'Materiál s ID {{materialId}} nebyl nalezen',
        'InvalidSearchCriteria': 'Neplatná kritéria vyhledávání: {{criteria}}',
        'ExternalSyncFailed': 'Synchronizace s externí službou selhala: {{details}}',
        'AttributeError': 'Chyba atributu: {{attribute}}',
        'SupplierLookupFailed': 'Vyhledání dodavatele selhalo: {{supplier}}',
        'CategoryError': 'Chyba kategorie: {{category}}',
        'UnitValidationFailed': 'Validace jednotky selhala: {{unit}}',
        'AbraIntegrationFailed': 'Integrace s ABRA selhala: {{details}}',
        'ShoptetSyncFailed': 'Synchronizace se Shoptet selhala: {{details}}',
        
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