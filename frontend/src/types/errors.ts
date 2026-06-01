// Re-export the auto-generated ErrorCodes enum so the entire frontend uses a single
// source of truth that's regenerated from the backend OpenAPI schema. Previously this
// file held a hand-maintained numeric enum that drifted from the backend, causing
// any new error code to render as "Nastala chyba (neznámý kód: <code>)" until the
// manual entry was added.
//
// The backend serializes ErrorCodes via JsonStringEnumConverter, so API responses
// carry errorCode as the string enum name. The generated enum is string-valued to
// match — direct equality comparisons (`response.errorCode === ErrorCodes.X`) work
// without any translation step.
import { ErrorCodes } from "../api/generated/api-client";

export { ErrorCodes };

export interface BaseResponse {
  success: boolean;
  errorCode?: ErrorCodes;
  params?: Record<string, string>;
}
