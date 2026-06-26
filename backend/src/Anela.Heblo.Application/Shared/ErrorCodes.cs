using System.Net;

namespace Anela.Heblo.Application.Shared;

/// <summary>
/// Enumeration of all possible error codes that can be returned by the API.
/// Error codes use a prefix-postfix system where:
/// - First two digits = Module prefix (00=general, 10=audit, 11=purchase, 12=manufacture, etc.)
/// - Last two digits = Specific error within that module
/// </summary>
public enum ErrorCodes
{
    // General errors (00XX)
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    ValidationError = 0001,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    RequiredFieldMissing = 0002,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidFormat = 0003,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidValue = 0004,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidDateRange = 0005,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ResourceNotFound = 0006,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    BusinessRuleViolation = 0007,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidOperation = 0008,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    DuplicateEntry = 0009,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    InternalServerError = 0010,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    DatabaseError = 0011,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    ConfigurationError = 0012,
    [HttpStatusCode(HttpStatusCode.Unauthorized)]
    Unauthorized = 0013,
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    Forbidden = 0014,
    [HttpStatusCode(HttpStatusCode.Unauthorized)]
    TokenExpired = 0015,
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    InsufficientPermissions = 0016,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    Exception = 0099,


    // Audit module errors (10XX) - reserved for future use
    // AuditError = 1001,

    // Purchase module errors (11XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PurchaseOrderNotFound = 1101,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    SupplierNotFound = 1102,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    StatusTransitionNotAllowed = 1103,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InsufficientStock = 1104,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidPurchaseOrderStatus = 1105,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidSupplier = 1106,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    PurchaseOrderUpdateFailed = 1107,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PurchaseOrderLineNotFound = 1108,

    // Manufacture module errors (12XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ManufacturingDataNotAvailable = 1201,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    ManufactureAnalysisCalculationFailed = 1202,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidAnalysisParameters = 1203,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InsufficientManufacturingData = 1204,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ManufactureTemplateNotFound = 1205,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidBatchSize = 1206,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    IngredientNotFoundInTemplate = 1207,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidIngredientAmount = 1208,
    [HttpStatusCode(HttpStatusCode.OK)]
    FixedProductsExceedAvailableVolume = 1209,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    OrderNotFound = 1210,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CannotUpdateCompletedOrder = 1211,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CannotUpdateCancelledOrder = 1212,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CannotScheduleInPast = 1213,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidScheduleDateOrder = 1214,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ManufacturedInventoryItemNotFound = 1215,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ManufacturedInventoryInsufficientStock = 1216,

    // Catalog module errors (13XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    CatalogItemNotFound = 1301,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ManufactureDifficultyNotFound = 1302,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    ManufactureDifficultyConflict = 1303,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    MarginCalculationError = 1304,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    DataAccessUnavailable = 1305,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ProductNotFound = 1306,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    MaterialNotFound = 1307,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidSearchCriteria = 1308,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    ExternalSyncFailed = 1309,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    AttributeError = 1310,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    SupplierLookupFailed = 1311,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CategoryError = 1312,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    UnitValidationFailed = 1313,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    AbraIntegrationFailed = 1314,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ShoptetSyncFailed = 1315,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    StockTakingFailed = 1316,

    // Transport module errors (14XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    TransportBoxNotFound = 1401,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    TransportBoxStateChangeError = 1402,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    TransportBoxCreationError = 1403,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    TransportBoxItemError = 1404,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    TransportBoxDuplicateActiveBoxFound = 1405,

    // Configuration module errors (15XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ConfigurationNotFound = 1501,

    // Journal module errors (16XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    JournalEntryNotFound = 1601,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidJournalTitle = 1602,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidJournalContent = 1603,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    JournalTagNotFound = 1604,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    JournalTagCreationFailed = 1605,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidJournalDateFilter = 1606,
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    JournalDeleteNotAllowed = 1607,
    [HttpStatusCode(HttpStatusCode.Unauthorized)]
    UnauthorizedJournalAccess = 1608,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    DuplicateJournalTag = 1609,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidJournalEntryData = 1610,

    // Analytics module errors (17XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    AnalysisDataNotAvailable = 1701,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    MarginCalculationFailed = 1702,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InsufficientData = 1703,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ProductNotFoundForAnalysis = 1704,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidReportPeriod = 1705,

    // FileStorage module errors (18XX)
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidUrlFormat = 1801,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidContainerName = 1802,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    FileDownloadFailed = 1803,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    BlobUploadFailed = 1804,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    BlobNotFound = 1805,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    FileTooLarge = 1806,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    UnsupportedFileType = 1807,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidBlobPath = 1808,

    // BackgroundJobs module errors (19XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    RecurringJobNotFound = 1901,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    RecurringJobUpdateFailed = 1902,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidCronExpression = 1903,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    RecurringJobDisabled = 1904,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    RecurringJobEnqueueFailed = 1905,

    // KnowledgeBase module errors (20XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    KnowledgeBaseFeedbackLogNotFound = 2001,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    KnowledgeBaseFeedbackAlreadySubmitted = 2002,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    KnowledgeBaseChunkNotFound = 2003,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    KnowledgeBaseAiUnavailable = 2004,

    // ShoptetOrders module errors (21XX)
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShoptetOrderInvalidSourceState = 2101,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ShoptetOrderNotFound = 2102,
    // Manual expedition single-order print
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ExpeditionOrderInvalidState = 2103,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ExpeditionOrderNotPrinted = 2104,

    // DataQuality module errors (22XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    DqtRunNotFound = 2201,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    DqtInvalidDateRange = 2202,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    DqtExternalServiceError = 2203,

    // Marketing Calendar errors (23XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    MarketingActionNotFound = 2301,
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    UnauthorizedMarketingAccess = 2302,
    [HttpStatusCode(HttpStatusCode.Forbidden)]
    MarketingCalendarAccessDenied = 2303,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    MarketingCalendarSyncFailed = 2304,

    // Article Generation errors (24XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ArticleNotFound = 2401,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    ArticleGenerationFailed = 2402,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    WebSearchUnavailable = 2403,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    StyleGuideFetchFailed = 2404,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    ArticleAlreadyGenerated = 2405,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ArticleNotGenerated = 2406,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    ArticleFeedbackAlreadySubmitted = 2407,

    // Leaflet module errors (25XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    LeafletChunkNotFound = 2501,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    LeafletFeedbackNotFound = 2502,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    LeafletFeedbackAlreadySubmitted = 2503,

    // Photobank errors (26XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PhotoNotFound = 2601,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PhotobankRootNotFound = 2602,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PhotobankRuleNotFound = 2603,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    PhotoTagCreationFailed = 2604,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    BulkTagFiltersRequired = 2605,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    BulkTagLimitExceeded = 2606,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    BulkTagInvalidRequest = 2607,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PhotobankTagNotFound = 2608,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    PhotobankInvalidRegexPattern = 2609,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PhotobankThumbnailNotFound = 2610,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PhotobankThumbnailThrottled = 2611,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PhotobankThumbnailAuthUnavailable = 2612,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    PhotobankThumbnailUpstream = 2613,

    // Smartsupp module errors (27XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    SmartsuppConversationNotFound = 2701,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    SmartsuppDraftReplyAiUnavailable = 2702,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    SmartsuppConversationEmpty = 2703,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    SmartsuppShoptetCustomerNotFound = 2704,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    SmartsuppVisitorNotFound = 2705,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    SmartsuppSendMessageUnavailable = 2706,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    SmartsuppAgentMappingNotFound = 2707,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    SmartsuppCloseConversationUnavailable = 2708,

    // Inventory module errors (28XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    LotNotFound = 2801,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    MaterialContainerNotFound = 2802,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    LotAlreadyExists = 2803,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    InventoryMaterialNotFound = 2804,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InventoryMaterialInvalidType = 2805,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    LotHasEans = 2806,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MaterialContainerCodeExists = 2807,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MaterialContainerCodeInvalidFormat = 2808,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    UnknownMaterialContainerCode = 2809,

    // WeatherForecast module errors (29XX)
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    WeatherForecastUnavailable = 2901,

    // ShipmentLabels module errors (2902–29XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ShipmentLabelsNoShipmentFound = 2902,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentLabelsNotGenerated = 2903,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    ShipmentAlreadyExists = 2905,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentCarrierNotResolved = 2906,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ShipmentCreationFailed = 2907,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentLabelNotReady = 2908,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    ShipmentOrderWeightUnavailable = 2909,

    // Packaging module errors (30XX)
    [HttpStatusCode(HttpStatusCode.Conflict)]
    OrderNotInPackingState = 3001,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ShipmentCancelFailed = 3002,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    NoShipmentToReset = 3003,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PackageLabelNotFound = 3004,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PackageLabelDownloadFailed = 3005,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PackageNotFound = 3006,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidPackageCount = 3007,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PackingCompletionFailed = 3008,
    [HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
    PackingUserNotEligible = 3009,

    // CatalogDocuments module errors (31XX)
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CatalogDocumentInvalidTypeCode = 3101,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CatalogDocumentLotRequired = 3102,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    CatalogDocumentFolderNotFound = 3103,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    CatalogDocumentFolderMultipleMatches = 3104,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    CatalogDocumentFileMissing = 3105,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    CatalogDocumentGraphError = 3106,

    // Authorization module errors (32XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    AuthorizationGroupNotFound = 3201,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    AuthorizationUserNotFound = 3202,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    AuthorizationInvalidPermission = 3203,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    AuthorizationGroupCycleDetected = 3204,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    AuthorizationDuplicateGroupName = 3206,

    // External Service errors (90XX)
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ExternalServiceError = 9001,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    FlexiApiError = 9002,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ShoptetApiError = 9003,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PaymentGatewayError = 9004,
    [HttpStatusCode(HttpStatusCode.BadGateway)]
    ErpGatewayError = 9005,
}