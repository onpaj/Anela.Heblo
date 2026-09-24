import { readApiErrorEnvelope } from "../../api/apiErrorEnvelope";
import { ErrorCodes } from "../../api/generated/api-client";
import { resolveSwaggerErrorMessage } from "../../utils/errorHandler";

/**
 * Returns the server's list of missing materials when a manufacture confirmation was
 * refused for insufficient stock, or `undefined` so the caller keeps its generic message.
 */
export const resolveStockShortageMessage = (error: unknown): string | undefined =>
  readApiErrorEnvelope(error)?.errorCode === ErrorCodes.ManufactureInsufficientMaterialStock
    ? resolveSwaggerErrorMessage(error)
    : undefined;
