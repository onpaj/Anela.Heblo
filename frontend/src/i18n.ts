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
      manufacture: {
        states: {
          Draft: "Návrh",
          Planned: "Naplánováno",
          SemiProductManufactured: "Meziprodukt",
          Completed: "Dokončeno",
          Cancelled: "Zrušeno"
        },
        auditActions: {
          StateChanged: "Změna stavu",
          QuantityChanged: "Změna množství",
          DateChanged: "Změna data",
          ResponsiblePersonAssigned: "Přiřazení odpovědné osoby",
          NoteAdded: "Přidání poznámky",
          OrderCreated: "Vytvoření zakázky"
        },
        scheduleUpdatedSuccessfully: "Rozpis výroby byl úspěšně aktualizován"
      },
      transport: {
        states: {
          New: "Nový",
          Opened: "Otevřený",
          InTransit: "V přepravě",
          Received: "Přijatý",
          InSwap: "Ve výměně",
          Stocked: "Naskladněný",
          Closed: "Uzavřený",
          Error: "Chyba",
          Reserve: "V rezervě"
        }
      },
      dashboard: {
        tileCategories: {
          Manufacture: "Výroba",
          System: "Systém",
          Warehouse: "Sklad",
          Purchase: "Nákup",
          Finance: "Finance",
          Orders: "Objednávky",
          Logistics: "Logistika",
          Analytics: "Analytika",
          DataQuality: "Kvalita dat",
          Error: "Chyba"
        },
        tileSizes: {
          Small: "S",
          Medium: "M",
          Large: "L"
        }
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
        FixedProductsExceedAvailableVolume: "Fixní produkty vyžadují více objemu ({volumeUsedByFixed} g) než je k dispozici ({availableVolume} g). Nedostatek: {deficit} g.",
        OrderNotFound: "Zakázka nenalezena",
        CannotUpdateCompletedOrder: "Nelze upravit rozpis dokončených zakázek",
        CannotUpdateCancelledOrder: "Nelze upravit rozpis zrušených zakázek", 
        CannotScheduleInPast: "Nelze naplánovat výrobu do minulosti",
        InvalidScheduleDateOrder: "Datum výroby polotovaru nemůže být po datu dokončení produktu",
        ManufacturedInventoryItemNotFound: "Položka skladu výroby nenalezena (ID: {id})",
        ManufacturedInventoryInsufficientStock: "Nedostatečné zásoby ve skladu výroby. Dostupné: {available}",

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
        StockTakingFailed: "Inventura selhala: {{details}}",

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

        // BackgroundJobs module errors
        RecurringJobNotFound: "Opakovaná úloha nenalezena",
        RecurringJobUpdateFailed: "Aktualizace opakované úlohy selhala",
        InvalidCronExpression: "Neplatný výraz CRON",

        // KnowledgeBase module errors
        KnowledgeBaseFeedbackLogNotFound: "Záznam zpětné vazby nenalezen",
        KnowledgeBaseFeedbackAlreadySubmitted: "Zpětná vazba již byla odeslána",
        KnowledgeBaseChunkNotFound: "Fragment znalostní báze nebyl nalezen (ID: {id})",
        KnowledgeBaseAiUnavailable: "Služba AI je dočasně nedostupná. Zkuste to prosím znovu.",

        // Leaflet module errors
        LeafletChunkNotFound: "Fragment letáku nebyl nalezen",
        LeafletFeedbackNotFound: "Zpětná vazba k letáku nebyla nalezena",
        LeafletFeedbackAlreadySubmitted: "Zpětná vazba k letáku již byla odeslána",

        // ShoptetOrders module errors
        ShoptetOrderInvalidSourceState: "Objednávku nelze zablokovat – není ve povoleném stavu",
        ShoptetOrderNotFound: "Objednávka nebyla nalezena",

        // Marketing Calendar module errors
        MarketingActionNotFound: "Marketingová akce nebyla nalezena",
        UnauthorizedMarketingAccess: "Nemáte oprávnění k této marketingové akci",
        MarketingCalendarAccessDenied: "Nemáte oprávnění zapisovat do marketingového kalendáře. Musíte být členem marketingové skupiny.",
        MarketingCalendarSyncFailed: "Nepodařilo se kontaktovat Outlook kalendář. Zkuste to prosím znovu.",

        // Photobank module errors
        PhotoNotFound: "Fotka nebyla nalezena",
        PhotobankRootNotFound: "Kořenový adresář fotobanka nebyl nalezen",
        PhotobankRuleNotFound: "Pravidlo tagu nebylo nalezeno",
        PhotoTagCreationFailed: "Vytvoření tagu fotky selhalo.",
        BulkTagFiltersRequired: "Pro hromadné tagování musí být aktivní alespoň jeden filtr.",
        BulkTagLimitExceeded: "Filtr odpovídá příliš mnoha fotkám ({{Count}}). Upřesněte filtry (max {{Limit}}).",
        BulkTagInvalidRequest: "Neplatný požadavek pro hromadné tagování.",
        PhotobankTagNotFound: "Tag fotobanka nebyl nalezen.",
        PhotobankInvalidRegexPattern: "Neplatný regulární výraz.",

        // Smartsupp module errors
        SmartsuppConversationNotFound: "Konverzace Smartsupp nebyla nalezena",
        SmartsuppDraftReplyAiUnavailable: "AI služba je momentálně nedostupná. Zkuste to prosím znovu.",
        SmartsuppConversationEmpty: "Konverzace neobsahuje zprávu zákazníka.",
        SmartsuppShoptetCustomerNotFound: "Zákazník v Shoptetu nenalezen.",
        SmartsuppVisitorNotFound: "Návštěvník nebyl nalezen.",
        SmartsuppSendMessageUnavailable: "Odeslání zprávy selhalo. Zkuste to prosím znovu.",
        SmartsuppAgentMappingNotFound: "Váš uživatelský účet nemá přiřazený Smartsupp agent. Doplňte mapování v Smartsupp:AgentMap.",

        // Inventory module errors
        LotNotFound: "Šarže nebyla nalezena.",
        EanNotFound: "EAN kód nebyl nalezen.",
        LotAlreadyExists: "Šarže s tímto kódem již existuje.",
        InventoryMaterialNotFound: "Materiál skladu nebyl nalezen.",
        InventoryMaterialInvalidType: "Neplatný typ materiálu skladu.",
        LotHasEans: "Šarži nelze smazat, protože obsahuje EAN kódy.",

        // Article Generation errors
        ArticleNotFound: "Článek nebyl nalezen (ID: {{id}})",
        ArticleGenerationFailed: "Generování článku selhalo. Zkuste to prosím znovu.",
        WebSearchUnavailable: "Webové vyhledávání je dočasně nedostupné.",
        StyleGuideFetchFailed: "Nepodařilo se načíst stylový průvodce.",
        ArticleAlreadyGenerated: "Článek již byl vygenerován.",
        ArticleNotGenerated: "Článek ještě nebyl vygenerován.",
        ArticleFeedbackAlreadySubmitted: "Zpětná vazba k tomuto článku již byla odeslána.",

        // Shoptet data drift errors
        DqtProductPairingFailed: "Chyba při párování produktů: {{details}}",
        DqtStockWriteBackFailed: "Chyba zpětného zápisu skladu: {{details}}",

        // Weather forecast errors
        WeatherForecastUnavailable: "Předpověď počasí je momentálně nedostupná.",

        // ShipmentLabels module errors
        ShipmentLabelsNoShipmentFound: "Zásilka k objednávce nebyla nalezena.",
        ShipmentLabelsNotGenerated: "Štítky zásilek nebyly dosud vygenerovány.",
        ShipmentAlreadyExists: "Zásilka pro tuto objednávku již existuje.",
        ShipmentCarrierNotResolved: "Nepodařilo se určit dopravce pro objednávku.",
        ShipmentCreationFailed: "Vytvoření zásilky se nezdařilo.",
        ShipmentLabelNotReady: "Štítek zásilky ještě není připraven.",
        ShipmentOrderWeightUnavailable: "Hmotnost objednávky není dostupná.",

        // Packaging module errors
        OrderNotInPackingState: "Objednávka není ve stavu Balí se — zásilku nelze vytvořit.",
        ShipmentCancelFailed: "Zrušení zásilky u dopravce se nezdařilo.",
        NoShipmentToReset: "K této objednávce neexistuje žádná zásilka, kterou by bylo možné resetovat.",
        PackageLabelNotFound: "Štítek pro tento balík nebyl nalezen.",
        PackageLabelDownloadFailed: "Stažení PDF štítku od dopravce se nezdařilo.",

        // External Service errors
        ExternalServiceError: "Chyba externí služby",
        FlexiApiError: "Chyba ABRA Flexi API",
        ShoptetApiError: "Chyba Shoptet API",
        PaymentGatewayError: "Chyba platební brány",
        ErpGatewayError: "Chyba ERP brány (časový limit nebo nedostupnost)",
      },
      dataQuality: {
        testTypes: {
          IssuedInvoiceComparison: "Porovnání faktur",
          ProductPairing: "Párování produktů",
          StockWriteBackReconciliation: "Zpětný zápis skladu",
        },
        productPairingMismatches: {
          MissingInErp: "Chybí v ERP",
          MissingInShoptet: "Chybí v Shoptet",
          PairCodeUnresolved: "Nespárovaný párový kód",
        },
        stockWriteBackMismatches: {
          OperationFailed: "Operace selhala",
          OperationStuck: "Operace zaseknutá",
          StockTakingErrored: "Chyba inventury",
        },
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
      manufacture: {
        states: {
          Draft: "Draft",
          Planned: "Planned",
          SemiProductManufactured: "Semi-product Manufactured",
          Completed: "Completed",
          Cancelled: "Cancelled"
        },
        auditActions: {
          StateChanged: "State Changed",
          QuantityChanged: "Quantity Changed",
          DateChanged: "Date Changed",
          ResponsiblePersonAssigned: "Responsible Person Assigned",
          NoteAdded: "Note Added",
          OrderCreated: "Order Created"
        }
      },
      transport: {
        states: {
          New: "New",
          Opened: "Opened",
          InTransit: "In Transit",
          Received: "Received",
          InSwap: "In Swap",
          Stocked: "Stocked",
          Closed: "Closed",
          Error: "Error",
          Reserve: "In Reserve"
        }
      },
      dashboard: {
        tileCategories: {
          Manufacture: "Manufacturing",
          System: "System",
          Warehouse: "Warehouse",
          Purchase: "Purchase",
          Finance: "Finance",
          Orders: "Orders",
          Logistics: "Logistics",
          Analytics: "Analytics",
          DataQuality: "Data Quality",
          Error: "Error"
        },
        tileSizes: {
          Small: "S",
          Medium: "M",
          Large: "L"
        }
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

        // DataQuality module errors
        DqtRunNotFound: "Data quality test run not found (ID: {{runId}})",
        DqtInvalidDateRange: "Invalid date range: DateFrom must be before or equal to DateTo",
        DqtExternalServiceError: "External service error during data quality test: {{service}}",

        // Shoptet data drift errors
        DqtProductPairingFailed: "Product pairing failed: {{details}}",
        DqtStockWriteBackFailed: "Stock write-back reconciliation failed: {{details}}",

        // Article Generation errors
        ArticleNotFound: "Article not found (ID: {{id}})",
        ArticleGenerationFailed: "Article generation failed. Please try again.",
        WebSearchUnavailable: "Web search is temporarily unavailable.",
        StyleGuideFetchFailed: "Failed to load style guide.",
        ArticleAlreadyGenerated: "Article has already been generated.",
        ArticleNotGenerated: "Article has not been generated yet.",
        ArticleFeedbackAlreadySubmitted: "Feedback for this article has already been submitted.",
      },
      dataQuality: {
        testTypes: {
          IssuedInvoiceComparison: "Invoice Comparison",
          ProductPairing: "Product Pairing",
          StockWriteBackReconciliation: "Stock Write-Back Reconciliation",
        },
        productPairingMismatches: {
          MissingInErp: "Missing in ERP",
          MissingInShoptet: "Missing in Shoptet",
          PairCodeUnresolved: "Unresolved pair code",
        },
        stockWriteBackMismatches: {
          OperationFailed: "Operation failed",
          OperationStuck: "Operation stuck",
          StockTakingErrored: "Stock-taking errored",
        },
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
    lng: "cs", // Force Czech language
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
