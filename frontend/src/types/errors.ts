/**
 * Error codes matching backend ErrorCodes enum
 */
export enum ErrorCodes {
  // Validation errors (1000-1999)
  ValidationError = 1000,
  RequiredFieldMissing = 1001,
  InvalidFormat = 1002,
  InvalidValue = 1003,
  InvalidDateRange = 1004,

  // Not Found errors (2000-2999)
  ResourceNotFound = 2000,
  PurchaseOrderNotFound = 2001,
  SupplierNotFound = 2002,
  CatalogItemNotFound = 2003,
  TransportBoxNotFound = 2004,
  ConfigurationNotFound = 2005,

  // Business Logic errors (3000-3999)
  BusinessRuleViolation = 3000,
  InvalidOperation = 3001,
  StatusTransitionNotAllowed = 3002,
  InsufficientStock = 3003,
  DuplicateEntry = 3004,

  // External Service errors (4000-4999)
  ExternalServiceError = 4000,
  FlexiApiError = 4001,
  ShoptetApiError = 4002,
  PaymentGatewayError = 4003,

  // System errors (5000-5999)
  InternalServerError = 5000,
  DatabaseError = 5001,
  ConfigurationError = 5002,

  // Authentication/Authorization errors (6000-6999)
  Unauthorized = 6000,
  Forbidden = 6001,
  TokenExpired = 6002,
}

/**
 * Base response structure matching backend BaseResponse
 */
export interface BaseResponse {
  success: boolean;
  errorCode?: ErrorCodes;
  params?: Record<string, string>;
}