/**
 * Error codes matching backend ErrorCodes enum
 * Uses prefix-postfix system: First two digits = Module prefix, Last two digits = Specific error
 */
export enum ErrorCodes {
  // General errors (00XX)
  ValidationError = 1,
  RequiredFieldMissing = 2,
  InvalidFormat = 3,
  InvalidValue = 4,
  InvalidDateRange = 5,
  ResourceNotFound = 6,
  BusinessRuleViolation = 7,
  InvalidOperation = 8,
  DuplicateEntry = 9,
  InternalServerError = 10,
  DatabaseError = 11,
  ConfigurationError = 12,
  Unauthorized = 13,
  Forbidden = 14,
  TokenExpired = 15,
  Exception = 99,

  // Purchase module errors (11XX)
  PurchaseOrderNotFound = 1101,
  SupplierNotFound = 1102,
  StatusTransitionNotAllowed = 1103,
  InsufficientStock = 1104,

  // Manufacture module errors (12XX)
  ManufacturingDataNotAvailable = 1201,
  ManufactureAnalysisCalculationFailed = 1202,
  InvalidAnalysisParameters = 1203,
  InsufficientManufacturingData = 1204,

  // Catalog module errors (13XX)
  CatalogItemNotFound = 1301,
  ManufactureDifficultyNotFound = 1302,
  ManufactureDifficultyConflict = 1303,
  MarginCalculationError = 1304,
  DataAccessUnavailable = 1305,
  ProductNotFound = 1306,
  MaterialNotFound = 1307,
  InvalidSearchCriteria = 1308,
  ExternalSyncFailed = 1309,
  AttributeError = 1310,
  SupplierLookupFailed = 1311,
  CategoryError = 1312,
  UnitValidationFailed = 1313,
  AbraIntegrationFailed = 1314,
  ShoptetSyncFailed = 1315,

  // Transport module errors (14XX)
  TransportBoxNotFound = 1401,
  TransportBoxStateChangeError = 1402,
  TransportBoxCreationError = 1403,
  TransportBoxItemError = 1404,

  // Configuration module errors (15XX)
  ConfigurationNotFound = 1501,

  // External Service errors (90XX)
  ExternalServiceError = 9001,
  FlexiApiError = 9002,
  ShoptetApiError = 9003,
  PaymentGatewayError = 9004,
}

/**
 * Base response structure matching backend BaseResponse
 */
export interface BaseResponse {
  success: boolean;
  errorCode?: ErrorCodes;
  params?: Record<string, string>;
}