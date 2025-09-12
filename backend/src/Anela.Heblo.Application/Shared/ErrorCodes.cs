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
    [HttpStatusCode(HttpStatusCode.BadRequest)]
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

    // External Service errors (90XX)
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ExternalServiceError = 9001,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    FlexiApiError = 9002,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    ShoptetApiError = 9003,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    PaymentGatewayError = 9004,
}