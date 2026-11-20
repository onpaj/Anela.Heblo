import { ErrorCodes, SwaggerException } from '../generated/api-client';
import { handleApiError } from '../../utils/errorHandler';
import { readApiErrorEnvelope } from '../apiErrorEnvelope';

/**
 * The generated client throws on every non-200, so the real errorCode of a failed reprint
 * lives only in the thrown exception's raw JSON body. Anything unparseable falls back to
 * the generic internal-error message.
 */
export function resolveReprintErrorMessage(error: unknown): string {
  const envelope = readApiErrorEnvelope(error);
  const isBareForbidden = !envelope && error instanceof SwaggerException && error.status === 403;
  const fallbackCode = isBareForbidden ? ErrorCodes.Forbidden : ErrorCodes.InternalServerError;
  return handleApiError({
    success: false,
    errorCode: envelope?.errorCode ?? fallbackCode,
    params: envelope?.params,
  });
}
