import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import LanguageDetector from "i18next-browser-languagedetector";

const resources = {
  cs: {
    translation: {
      navigation: {
        catalog: "Katalog",
        manufacture: "Výroba",
        purchase: "Nákup",
        transport: "Doprava",
        invoices: "Faktury",
      },
      common: {
        openMenu: "Otevřít menu",
        search: "Hledat...",
        loading: "Načítá se...",
        welcome: "Vítejte v Anela Heblo",
        appLoading: "Aplikace se načítá...",
      },
      errors: {
        // String keys for ErrorCodes enum names (primary approach)
        // General errors
        ValidationError: "Chyba validace",
        RequiredFieldMissing: "Povinné pole chybí",
        InvalidFormat: "Nesprávný formát",
        InvalidValue: "Neplatná hodnota",
        InvalidDateRange: "Neplatný rozsah dat",
        ResourceNotFound: "Zdroj nenalezen",
        BusinessRuleViolation: "Porušení obchodního pravidla",
        InvalidOperation: "Neplatná operace",
        DuplicateEntry: "Duplicitní záznam",
        InternalServerError: "Interní chyba serveru",
        DatabaseError: "Chyba databáze",
        ConfigurationError: "Chyba konfigurace",
        Unauthorized: "Neautorizovaný přístup",
        Forbidden: "Přístup zakázán",
        TokenExpired: "Token vypršel",
        Exception: "Výjimka aplikace",

        // Purchase module errors
        PurchaseOrderNotFound: "Objednávka nenalezena (ID: {id})",
        SupplierNotFound: "Dodavatel nenalezen (ID: {id})",
        StatusTransitionNotAllowed: "Změna stavu není povolena",
        InsufficientStock: "Nedostatečné množství na skladě",
        InvalidPurchaseOrderStatus: "Neplatný stav objednávky: {status}",
        InvalidSupplier: "Neplatný dodavatel: {supplierName}",
        PurchaseOrderUpdateFailed:
          "Aktualizace objednávky {orderNumber} selhala: {message}",

        // Manufacture module errors
        ManufacturingDataNotAvailable:
          "Výrobní data nejsou k dispozici pro analýzu",
        ManufactureAnalysisCalculationFailed:
          "Výpočet výrobní analýzy selhal: {reason}",
        InvalidAnalysisParameters: "Neplatné parametry analýzy: {parameters}",
        InsufficientManufacturingData:
          "Nedostatečná data pro spolehlivou výrobní analýzu",
        ManufactureTemplateNotFound: "Výrobní šablona nebyla nalezena",
        InvalidBatchSize: "Neplatná velikost dávky",
        IngredientNotFoundInTemplate: "Ingredience nebyla nalezena v šabloně",
        InvalidIngredientAmount: "Neplatné množství ingredience",

        // Catalog module errors
        CatalogItemNotFound: "Položka katalogu nenalezena (ID: {id})",
        ManufactureDifficultyNotFound:
          "Nastavení obtížnosti výroby nenalezeno (ID: {id})",
        ManufactureDifficultyConflict:
          "Konflikt při ukládání obtížnosti výroby",
        MarginCalculationError: "Chyba při výpočtu marží",
        DataAccessUnavailable: "Zdroj dat není dostupný",
        ProductNotFound: "Produkt s kódem {{productCode}} nebyl nalezen",
        MaterialNotFound: "Materiál s ID {{materialId}} nebyl nalezen",
        InvalidSearchCriteria: "Neplatná kritéria vyhledávání: {{criteria}}",
        ExternalSyncFailed:
          "Synchronizace s externí službou selhala: {{details}}",
        AttributeError: "Chyba atributu: {{attribute}}",
        SupplierLookupFailed: "Vyhledání dodavatele selhalo: {{supplier}}",
        CategoryError: "Chyba kategorie: {{category}}",
        UnitValidationFailed: "Validace jednotky selhala: {{unit}}",
        AbraIntegrationFailed: "Integrace s ABRA selhala: {{details}}",
        ShoptetSyncFailed: "Synchronizace se Shoptet selhala: {{details}}",

        // Transport module errors
        TransportBoxNotFound: "Přepravní box nenalezen (ID: {id})",
        TransportBoxStateChangeError: "Chyba při změně stavu přepravního boxu",
        TransportBoxCreationError: "Chyba při vytváření přepravního boxu",
        TransportBoxItemError: "Chyba při práci s položkami v přepravním boxu",
        TransportBoxDuplicateActiveBoxFound:
          "Box s číslem {code} již existuje a je stále aktivní",

        // Configuration module errors
        ConfigurationNotFound: "Konfigurace nenalezena (ID: {id})",

        // Analytics module errors
        AnalysisDataNotAvailable:
          "Data pro analýzu {{product}} nejsou k dispozici pro období {{period}}",
        MarginCalculationFailed: "Výpočet marží selhal: {{reason}}",
        InsufficientData:
          "Nedostatečná data pro analýzu: minimum požadovaného období {{requiredPeriod}}",
        ProductNotFoundForAnalysis:
          "Produkt {{productId}} nebyl nalezen pro analýzu",
        InvalidReportPeriod: "Neplatné období sestavy: {{period}}",

        // Journal module errors
        JournalEntryNotFound: "Záznam z deníku nebyl nalezen (ID: {{entryId}})",
        InvalidJournalTitle:
          "Neplatný titulek deníku - musí mít {{minLength}}-{{maxLength}} znaků",
        InvalidJournalContent: "Obsah deníku nemůže být prázdný",
        JournalTagNotFound: "Značka deníku nenalezena ({{tagName}})",
        JournalTagCreationFailed: "Vytvoření značky selhalo: {{tagName}}",
        InvalidJournalDateFilter: "Neplatný filtr data: {{dateFilter}}",
        JournalDeleteNotAllowed:
          "Mazání záznamu deníku není povoleno: {{reason}}",
        UnauthorizedJournalAccess:
          "Můžete přistupovat pouze k vlastním záznamům ({{resource}})",
        DuplicateJournalTag: "Značka s názvem {{tagName}} již existuje",
        InvalidJournalEntryData: "Neplatná data záznamu deníku: {{field}}",

        // FileStorage module errors
        InvalidUrlFormat: "Neplatný formát URL",
        InvalidContainerName: "Neplatný název kontejneru",
        FileDownloadFailed: "Stahování souboru selhalo",
        BlobUploadFailed: "Nahrávání souboru selhalo",
        BlobNotFound: "Soubor nenalezen",
        FileTooLarge: "Soubor je příliš velký",
        UnsupportedFileType: "Nepodporovaný typ souboru",

        // External Service errors
        ExternalServiceError: "Chyba externí služby",
        FlexiApiError: "Chyba ABRA Flexi API",
        ShoptetApiError: "Chyba Shoptet API",
        PaymentGatewayError: "Chyba platební brány",
      },
    },
  },
  en: {
    translation: {
      navigation: {
        catalog: "Catalog",
        manufacture: "Manufacture",
        purchase: "Purchase",
        transport: "Transport",
        invoices: "Invoices",
      },
      common: {
        openMenu: "Open menu",
        search: "Search...",
        loading: "Loading...",
        welcome: "Welcome to Anela Heblo",
        appLoading: "Application is loading...",
      },
      errors: {
        // Analytics module errors
        AnalysisDataNotAvailable:
          "No analysis data available for {{product}} in {{period}}",
        MarginCalculationFailed: "Margin calculation failed: {{reason}}",
        InsufficientData:
          "Insufficient data for analysis: minimum required period {{requiredPeriod}}",
        ProductNotFoundForAnalysis:
          "Product {{productId}} not found for analysis",
        InvalidReportPeriod: "Invalid report period: {{period}}",

        // Journal module errors
        JournalEntryNotFound: "Journal entry not found (ID: {{entryId}})",
        InvalidJournalTitle:
          "Invalid journal title - must be {{minLength}}-{{maxLength}} characters",
        InvalidJournalContent: "Journal content cannot be empty",
        JournalTagNotFound: "Journal tag not found ({{tagName}})",
        JournalTagCreationFailed: "Failed to create tag: {{tagName}}",
        InvalidJournalDateFilter: "Invalid date filter: {{dateFilter}}",
        JournalDeleteNotAllowed: "Cannot delete journal entry: {{reason}}",
        UnauthorizedJournalAccess: "You can only access your own {{resource}}",
        DuplicateJournalTag: "Tag with name {{tagName}} already exists",
        InvalidJournalEntryData: "Invalid journal entry data: {{field}}",
      },
    },
  },
};

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: "cs", // Czech as primary language per design doc
    debug: process.env.NODE_ENV === "development",
    interpolation: {
      escapeValue: false,
    },
    detection: {
      order: ["localStorage", "navigator", "htmlTag"],
      caches: ["localStorage"],
    },
  });

export default i18n;
