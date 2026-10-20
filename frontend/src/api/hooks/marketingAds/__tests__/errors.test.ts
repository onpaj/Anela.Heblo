import { SwaggerException } from '../../../generated/api-client';
import { describeAdsError, isForbidden, isVersionConflict } from '../errors';

const swagger = (status: number, body: unknown = ''): SwaggerException =>
  new SwaggerException('HTTP error', status, typeof body === 'string' ? body : JSON.stringify(body), {}, null);

describe('isVersionConflict', () => {
  it('recognises a bare HTTP 409 that carries no error code', () => {
    expect(isVersionConflict(swagger(409))).toBe(true);
  });

  it.each([
    'MarketingAdsProposalVersionMismatch',
    'MarketingAdsProposalNotPending',
    'MarketingAdsProposalExpired',
    'MarketingAdsConcurrentChange',
  ])('recognises the typed DTO thrown without an HTTP status for %s', (errorCode) => {
    expect(isVersionConflict({ success: false, errorCode })).toBe(true);
  });

  it('recognises a conflict error code inside a non-409 response body', () => {
    expect(isVersionConflict(swagger(400, { success: false, errorCode: 'MarketingAdsProposalNotPending' }))).toBe(true);
  });

  it('a 409 carrying MarketingAdsAlreadyApprovedByPrincipal is not a version conflict', () => {
    const error = swagger(409, { success: false, errorCode: 'MarketingAdsAlreadyApprovedByPrincipal' });

    expect(isVersionConflict(error)).toBe(false);
  });

  it.each(['MarketingAdsNotRevertible', 'MarketingAdsNoExecutor'])('a 409 carrying %s is not a version conflict', (errorCode) => {
    expect(isVersionConflict(swagger(409, { success: false, errorCode }))).toBe(false);
  });

  it('does not treat other failures as conflicts', () => {
    expect(isVersionConflict(swagger(400, { success: false, errorCode: 'ValidationError' }))).toBe(false);
    expect(isVersionConflict(new Error('Failed to fetch'))).toBe(false);
    expect(isVersionConflict(undefined)).toBe(false);
  });
});

describe('isForbidden', () => {
  it.each(['Forbidden', 'MarketingAdsApprovalChannelDenied', 'MarketingAdsNeedsWebApproval', 'MarketingAdsNotProposalOwner'])(
    'recognises the error code %s',
    (errorCode) => {
      expect(isForbidden({ success: false, errorCode })).toBe(true);
    },
  );

  it('a 403 with no envelope is forbidden', () => {
    expect(isForbidden(swagger(403))).toBe(true);
  });

  it('a 403 carrying an unrelated error code is not treated as forbidden approval', () => {
    expect(isForbidden(swagger(403, { success: false, errorCode: 'ValidationError' }))).toBe(false);
  });

  it('does not treat a conflict as forbidden', () => {
    expect(isForbidden(swagger(409))).toBe(false);
  });
});

describe('describeAdsError', () => {
  it('falls back to the Error message when there is no error envelope', () => {
    expect(describeAdsError(new Error('Síť není dostupná'))).toBe('Síť není dostupná');
  });

  it('never returns an empty string', () => {
    expect(describeAdsError({})).not.toBe('');
  });
});
