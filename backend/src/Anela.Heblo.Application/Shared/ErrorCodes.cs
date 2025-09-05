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
    ValidationError = 0001,
    RequiredFieldMissing = 0002,
    InvalidFormat = 0003,
    InvalidValue = 0004,
    InvalidDateRange = 0005,
    ResourceNotFound = 0006,
    BusinessRuleViolation = 0007,
    InvalidOperation = 0008,
    DuplicateEntry = 0009,
    InternalServerError = 0010,
    DatabaseError = 0011,
    ConfigurationError = 0012,
    Unauthorized = 0013,
    Forbidden = 0014,
    TokenExpired = 0015,

    // Audit module errors (10XX) - reserved for future use
    // AuditError = 1001,

    // Purchase module errors (11XX)
    PurchaseOrderNotFound = 1101,
    SupplierNotFound = 1102,
    StatusTransitionNotAllowed = 1103,
    InsufficientStock = 1104,

    // Manufacture module errors (12XX) - reserved for future use
    // ManufactureError = 1201,

    // Catalog module errors (13XX)
    CatalogItemNotFound = 1301,
    ManufactureDifficultyNotFound = 1302,
    ManufactureDifficultyConflict = 1303,
    MarginCalculationError = 1304,
    DataAccessUnavailable = 1305,

    // Transport module errors (14XX)
    TransportBoxNotFound = 1401,
    TransportBoxStateChangeError = 1402,
    TransportBoxCreationError = 1403,
    TransportBoxItemError = 1404,
    TransportBoxDuplicateActiveBoxFound = 1405,

    // Configuration module errors (15XX)
    ConfigurationNotFound = 1501,

    // External Service errors (90XX)
    ExternalServiceError = 9001,
    FlexiApiError = 9002,
    ShoptetApiError = 9003,
    PaymentGatewayError = 9004
}