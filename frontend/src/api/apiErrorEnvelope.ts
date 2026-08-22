/**
 * Reading the `BaseResponse` error envelope off a failed generated-client call.
 *
 * The generated client resolves only on HTTP 200 — every other status goes through
 * `throwException`. A `BaseResponse` carrying `success: false` + `errorCode` therefore
 * never arrives as a resolved value for any endpoint whose error codes map to a non-200
 * status, so a caller that branches on `!response.success` is writing dead code: it never
 * sees the error code and always falls through to its generic message.
 *
 * The envelope survives the failure in one of two shapes:
 *  - a `SwaggerException`, whose `response` field holds the raw JSON body (the common case);
 *  - the parsed error result thrown directly by an endpoint that declares typed non-200
 *    branches, which carries `errorCode` as its own property.
 *
 * See `docs/development/api-client-generation.md`.
 */

import type { ErrorCodes } from './generated/api-client';

export interface ApiErrorEnvelope {
  errorCode?: ErrorCodes;
  params?: Record<string, string>;
}

/** The subset of a generated `BaseResponse` these helpers care about. */
export interface ApiEnvelopeResponse {
  success?: boolean;
  errorCode?: ErrorCodes;
  params?: { [key: string]: string } | undefined;
}

const asEnvelope = (candidate: unknown): ApiErrorEnvelope | null => {
  if (typeof candidate !== 'object' || candidate === null) {
    return null;
  }

  const { errorCode, params } = candidate as ApiErrorEnvelope;
  if (typeof errorCode !== 'string' || errorCode.length === 0) {
    return null;
  }

  // Typed as ErrorCodes to match the generated `BaseResponse`, but the value is whatever
  // the backend put on the wire — a code this client predates still arrives as a plain
  // string. Both `getErrorMessage()` and the hooks' message maps fall back on an
  // unrecognized value, so an unknown code degrades to a generic message rather than
  // throwing.
  //
  // `params` is normalized because the serialized envelope carries an explicit `null` when
  // there are none, and callers only ever guard against `undefined`.
  return { errorCode, params: params ?? undefined };
};

/**
 * Extracts the error envelope from a rejected generated-client call, or `null` when the
 * failure carries none (network error, non-JSON body, unstructured server error).
 */
export const readApiErrorEnvelope = (error: unknown): ApiErrorEnvelope | null => {
  const direct = asEnvelope(error);
  if (direct) {
    return direct;
  }

  const body = (error as { response?: unknown })?.response;
  if (typeof body !== 'string' || body.length === 0) {
    return null;
  }

  try {
    return asEnvelope(JSON.parse(body));
  } catch {
    return null;
  }
};

/**
 * Runs a generated-client call and normalizes both failure shapes — a thrown non-200 and a
 * resolved `success: false` — into a single `Error` whose message the caller derives from
 * the envelope. An unparseable failure yields an empty envelope so the caller can fall back
 * to its own generic message.
 */
export const callApi = async <T extends ApiEnvelopeResponse>(
  call: () => Promise<T>,
  toMessage: (envelope: ApiErrorEnvelope) => string,
): Promise<T> => {
  let response: T;

  try {
    response = await call();
  } catch (error) {
    throw new Error(toMessage(readApiErrorEnvelope(error) ?? {}));
  }

  if (response.success === false) {
    throw new Error(toMessage({ errorCode: response.errorCode, params: response.params }));
  }

  return response;
};
