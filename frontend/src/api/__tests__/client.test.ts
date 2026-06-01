import {
  getApiBaseUrl,
  getAuthenticatedFetch,
  setGlobalTokenProvider,
  clearTokenCache,
} from '../client';

// Mock dependencies
jest.mock('../../config/runtimeConfig', () => ({
  getConfig: () => ({
    apiUrl: 'https://api.test.example',
  }),
  shouldUseMockAuth: () => false,
}));

jest.mock('../../auth/mockAuth');

jest.mock('../../auth/e2eAuth', () => ({
  isE2ETestMode: () => false,
  getE2EAccessToken: () => null,
}));

describe('API Client - typed helpers', () => {
  let mockTokenProvider: jest.Mock;
  let originalFetch: typeof global.fetch;

  beforeAll(() => {
    originalFetch = global.fetch;
  });

  afterAll(() => {
    global.fetch = originalFetch;
  });

  beforeEach(() => {
    jest.clearAllMocks();
    clearTokenCache();

    // Setup mock token provider
    mockTokenProvider = jest.fn().mockResolvedValue({
      token: 'test-token',
      expiresOn: null,
    });
    setGlobalTokenProvider(mockTokenProvider);
  });

  describe('getApiBaseUrl', () => {
    it('returns a non-empty string', () => {
      const url = getApiBaseUrl();
      expect(typeof url).toBe('string');
      expect(url.length).toBeGreaterThan(0);
    });

    it('matches the URL passed to ApiClient constructor', () => {
      const baseUrl = getApiBaseUrl();
      expect(baseUrl).toBe('https://api.test.example');
    });
  });

  describe('getAuthenticatedFetch', () => {
    beforeEach(() => {
      // Reset fetch mock before each test
      jest.clearAllMocks();
    });

    it('attaches Authorization header', async () => {
      const fetchSpy = jest
        .spyOn(global, 'fetch')
        .mockResolvedValue(new Response(null, { status: 200 }));

      const authedFetch = getAuthenticatedFetch();
      await authedFetch('https://api.test.example/x', { method: 'GET' });

      expect(fetchSpy).toHaveBeenCalled();
      const callArgs = fetchSpy.mock.calls[0];
      const init = callArgs[1] as RequestInit;
      const headers = new Headers(init?.headers);

      // Should have Authorization header (mock auth service will provide it)
      expect(headers.get('Authorization')).toBeTruthy();

      fetchSpy.mockRestore();
    });

    it('does not throw on 500 response', async () => {
      jest
        .spyOn(global, 'fetch')
        .mockResolvedValueOnce(new Response(null, { status: 500 }));

      const response = await getAuthenticatedFetch()(
        'https://api.test.example/x'
      );

      expect(response.status).toBe(500);
    });

    it('merges caller-provided headers with auth headers', async () => {
      const fetchSpy = jest
        .spyOn(global, 'fetch')
        .mockResolvedValue(new Response(null, { status: 200 }));

      await getAuthenticatedFetch()('https://api.test.example/x', {
        headers: { 'X-Custom-Header': 'custom-value' },
      });

      const callArgs = fetchSpy.mock.calls[0];
      const init = callArgs[1] as RequestInit;
      const headers = new Headers(init?.headers);

      expect(headers.get('X-Custom-Header')).toBe('custom-value');

      fetchSpy.mockRestore();
    });

    it('returns a function that can be called multiple times', async () => {
      const fetchSpy = jest
        .spyOn(global, 'fetch')
        .mockResolvedValue(new Response(null, { status: 200 }));

      const authedFetch = getAuthenticatedFetch();

      await authedFetch('https://api.test.example/x');
      await authedFetch('https://api.test.example/y');

      expect(fetchSpy).toHaveBeenCalledTimes(2);

      fetchSpy.mockRestore();
    });

    it('includes Content-Type header for JSON requests', async () => {
      const fetchSpy = jest
        .spyOn(global, 'fetch')
        .mockResolvedValue(new Response(null, { status: 200 }));

      await getAuthenticatedFetch()('https://api.test.example/x');

      const callArgs = fetchSpy.mock.calls[0];
      const init = callArgs[1] as RequestInit;
      const headers = new Headers(init?.headers);

      expect(headers.get('Content-Type')).toBe('application/json');

      fetchSpy.mockRestore();
    });
  });
});
