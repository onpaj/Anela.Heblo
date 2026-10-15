import { readApiErrorEnvelope } from '../../apiErrorEnvelope';
import { extractErrorMessage, resolveSwaggerErrorMessage } from '../../../utils/errorHandler';

/** Error codes that mean "the proposal you are looking at is no longer the current, pending version". */
export const PROPOSAL_CONFLICT_ERROR_CODES: readonly string[] = [
  'MarketingAdsProposalVersionMismatch',
  'MarketingAdsProposalNotPending',
  'MarketingAdsProposalExpired',
  'MarketingAdsConcurrentChange',
];

/** Error codes that mean "this principal/channel may not approve" (spec 7.4). */
export const APPROVAL_FORBIDDEN_ERROR_CODES: readonly string[] = [
  'Forbidden',
  'MarketingAdsApprovalChannelDenied',
  'MarketingAdsNeedsWebApproval',
  'MarketingAdsNotProposalOwner',
];

const HTTP_CONFLICT = 409;
const HTTP_FORBIDDEN = 403;

const statusOf = (error: unknown): number | undefined => {
  const status = (error as { status?: unknown } | null | undefined)?.status;
  return typeof status === 'number' ? status : undefined;
};

const errorCodeOf = (error: unknown): string => String(readApiErrorEnvelope(error)?.errorCode ?? '');

// On the backend 409 also carries unrelated codes (already approved, not revertible, no executor),
// so a status alone only counts when the response names no error code at all.
const matchesCodeOrBareStatus = (error: unknown, codes: readonly string[], status: number): boolean => {
  const code = errorCodeOf(error);
  return codes.includes(code) || (code === '' && statusOf(error) === status);
};

export const isVersionConflict = (error: unknown): boolean =>
  matchesCodeOrBareStatus(error, PROPOSAL_CONFLICT_ERROR_CODES, HTTP_CONFLICT);

export const isForbidden = (error: unknown): boolean =>
  matchesCodeOrBareStatus(error, APPROVAL_FORBIDDEN_ERROR_CODES, HTTP_FORBIDDEN);

/** Czech message for any failure: translated ErrorCodes first, then the Error message, then a generic text. */
export const describeAdsError = (error: unknown): string =>
  resolveSwaggerErrorMessage(error) ?? extractErrorMessage(error);
