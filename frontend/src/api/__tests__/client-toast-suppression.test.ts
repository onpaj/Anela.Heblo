import {
  getAuthenticatedApiClient,
  setGlobalTokenProvider,
  setGlobalAuthRedirectHandler,
  setGlobalToastHandler,
  clearTokenCache,
} from "../client";

// Mock the config
jest.mock("../../config/runtimeConfig", () => ({
  getConfig: () => ({
    apiUrl: "http://localhost:5001",
  }),
  shouldUseMockAuth: () => false,
}));

// Mock auth modules
jest.mock("../../auth/e2eAuth", () => ({
  isE2ETestMode: () => false,
  getE2EAccessToken: () => null,
}));

describe("API Client - Toast suppression on /terminal routes", () => {
  let mockToastHandler: jest.Mock;
  let originalFetch: typeof global.fetch;

  beforeAll(() => {
    originalFetch = global.fetch;
  });

  afterAll(() => {
    global.fetch = originalFetch;
    // Restore to a real-looking location to avoid leaking into other suites
    Object.defineProperty(window, "location", {
      configurable: true,
      get: () => ({ pathname: "/" }),
    });
  });

  beforeEach(() => {
    jest.clearAllMocks();
    clearTokenCache();

    mockToastHandler = jest.fn();
    setGlobalToastHandler(mockToastHandler);
    setGlobalTokenProvider(
      jest.fn().mockResolvedValue({ token: "test-token", expiresOn: null }),
    );
    setGlobalAuthRedirectHandler(jest.fn());
  });

  const setPathname = (pathname: string) => {
    Object.defineProperty(window, "location", {
      configurable: true,
      get: () => ({ pathname }),
    });
  };

  const makeFailingRequest = async (): Promise<void> => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "Server error" }), {
        status: 500,
        headers: { "content-type": "application/json" },
      }),
    );

    const apiClient = getAuthenticatedApiClient();
    await (apiClient as any).http.fetch("http://localhost:5001/api/test", {
      method: "GET",
    });
  };

  it("does NOT call globalToastHandler when on /terminal route", async () => {
    setPathname("/terminal");

    await makeFailingRequest();

    expect(mockToastHandler).not.toHaveBeenCalled();
  });

  it("DOES call globalToastHandler when on a non-terminal route", async () => {
    setPathname("/dashboard");

    await makeFailingRequest();

    expect(mockToastHandler).toHaveBeenCalled();
  });

  it("does NOT call globalToastHandler when on /terminal/anything sub-route", async () => {
    setPathname("/terminal/transport-boxes");

    await makeFailingRequest();

    expect(mockToastHandler).not.toHaveBeenCalled();
  });
});
