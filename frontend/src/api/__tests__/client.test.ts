import {
  getApiBaseUrl,
  getAuthenticatedApiClient,
  getAuthenticatedFetch,
  setGlobalTokenProvider,
  setGlobalToastHandler,
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

    it('auth header wins over caller-provided Authorization header', async () => {
      const fetchSpy = jest
        .spyOn(global, 'fetch')
        .mockResolvedValue(new Response(null, { status: 200 }));

      // clearTokenCache and setGlobalTokenProvider are already set up in beforeEach
      await getAuthenticatedFetch()('https://api.test.example/x', {
        method: 'GET',
        headers: { Authorization: 'Bearer caller-bogus' },
      });

      const callArgs = fetchSpy.mock.calls[0];
      const init = callArgs[1] as RequestInit;
      const headers = new Headers(init?.headers);

      // The helper's real auth token must win; caller-bogus must not survive
      expect(headers.get('Authorization')).toBe('Bearer test-token');
      expect(headers.get('Authorization')).not.toBe('Bearer caller-bogus');

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

describe('getAuthenticatedApiClient toast suppression', () => {
  let toastHandler: jest.Mock;
  let originalFetch: typeof global.fetch;

  beforeAll(() => {
    originalFetch = global.fetch;
    // Ensure window.location.pathname is a non-terminal route for all tests in this suite
    Object.defineProperty(window, 'location', {
      configurable: true,
      get: () => ({ pathname: '/articles' }),
    });
  });

  afterAll(() => {
    global.fetch = originalFetch;
    Object.defineProperty(window, 'location', {
      configurable: true,
      get: () => ({ pathname: '/' }),
    });
  });

  beforeEach(() => {
    jest.resetAllMocks();
    clearTokenCache();

    toastHandler = jest.fn();
    setGlobalToastHandler(toastHandler);
    setGlobalTokenProvider(
      jest.fn().mockResolvedValue({ token: 'tok', expiresOn: null }),
    );
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('does NOT fire a toast on 409 when body is a structured BaseResponse with errorCode', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ success: false, errorCode: 'ArticleFeedbackAlreadySubmitted', params: { id: 'art-1' } }),
        { status: 409, headers: { 'Content-Type': 'application/json' } },
      ),
    ) as jest.Mock;

    const client = getAuthenticatedApiClient();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (client as any).http.fetch('https://api.test.example/api/test', { method: 'POST' });

    expect(toastHandler).not.toHaveBeenCalled();
  });

  it('DOES fire a toast on 500 with a structured BaseResponse body', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ success: false, errorCode: 'InternalServerError' }),
        { status: 500, headers: { 'Content-Type': 'application/json' } },
      ),
    ) as jest.Mock;

    const client = getAuthenticatedApiClient();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (client as any).http.fetch('https://api.test.example/api/test', { method: 'POST' });

    expect(toastHandler).toHaveBeenCalledTimes(1);
  });

  it('still fires a toast on 409 with an unstructured body', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response('Conflict', { status: 409, headers: { 'Content-Type': 'text/plain' } }),
    ) as jest.Mock;

    const client = getAuthenticatedApiClient();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (client as any).http.fetch('https://api.test.example/api/test', { method: 'POST' });

    expect(toastHandler).toHaveBeenCalledTimes(1);
  });
});
