import { renderHook } from '@testing-library/react';
import { useMeetingManagerPermission } from '../useMeetingManagerPermission';

jest.mock('../../../config/runtimeConfig', () => ({
  shouldUseMockAuth: jest.fn(),
}));
jest.mock('../../../auth/mockAuth', () => ({
  mockAuthService: { getUser: jest.fn() },
}));
jest.mock('@azure/msal-react', () => ({
  useMsal: jest.fn(),
}));

import { shouldUseMockAuth } from '../../../config/runtimeConfig';
import { mockAuthService } from '../../../auth/mockAuth';
import { useMsal } from '@azure/msal-react';

describe('useMeetingManagerPermission', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    // useMsal is always called first in the hook regardless of mock auth mode
    (useMsal as jest.Mock).mockReturnValue({ accounts: [] });
  });

  it('returns true when mock user has meeting_manager role', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(true);
    (mockAuthService.getUser as jest.Mock).mockReturnValue({ roles: ['meeting_manager'] });

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(true);
  });

  it('returns false when mock user does not have meeting_manager role', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(true);
    (mockAuthService.getUser as jest.Mock).mockReturnValue({ roles: ['heblo_user'] });

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(false);
  });

  it('returns false when mock user is null', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(true);
    (mockAuthService.getUser as jest.Mock).mockReturnValue(null);

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(false);
  });

  it('returns true when real MSAL account has meeting_manager role', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(false);
    (useMsal as jest.Mock).mockReturnValue({
      accounts: [{ idTokenClaims: { roles: ['meeting_manager'] } }],
    });

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(true);
  });

  it('returns false when real MSAL account lacks meeting_manager role', () => {
    (shouldUseMockAuth as jest.Mock).mockReturnValue(false);
    (useMsal as jest.Mock).mockReturnValue({
      accounts: [{ idTokenClaims: { roles: ['heblo_user'] } }],
    });

    const { result } = renderHook(() => useMeetingManagerPermission());

    expect(result.current).toBe(false);
  });
});
