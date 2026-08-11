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

const RECOVERY_KEY = "auth.recovery";

const seedRecoveryState = (count: number) => {
  sessionStorage.setItem(
    RECOVERY_KEY,
    JSON.stringify({ count, ts: Date.now() }),
  );
};

describe("API Client - auth recovery counter reset", () => {
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
    sessionStorage.clear();
    clearTokenCache();

    mockTokenProvider = jest.fn();
    setGlobalTokenProvider(mockTokenProvider);
    setGlobalAuthRedirectHandler(jest.fn());
    setGlobalToastHandler(jest.fn());
  });

  it("does not reset the recovery counter on a 2xx response sent without an Authorization header", async () => {
    // Dead session: token provider has nothing, so the request goes out anonymous.
    // An [AllowAnonymous] endpoint (e.g. feature flags) still answers 200 — that
    // proves nothing about authentication and must not defeat the escalation ladder.
    mockTokenProvider.mockResolvedValue(null);
    global.fetch = jest.fn().mockResolvedValue(
      new Response(JSON.stringify({ flags: {} }), { status: 200 }),
    );

    seedRecoveryState(1);

    const apiClient = getAuthenticatedApiClient();
    await (apiClient as any).http.fetch("http://localhost:5001/api/feature-flags", {
      method: "GET",
    });

    const state = sessionStorage.getItem(RECOVERY_KEY);
    expect(state).not.toBeNull();
    expect(JSON.parse(state!).count).toBe(1);
  });

  it("resets the recovery counter on a 2xx response sent with an Authorization header", async () => {
    mockTokenProvider.mockResolvedValue({
      token: "valid-access-token",
      expiresOn: null,
    });
    global.fetch = jest.fn().mockResolvedValue(
      new Response(JSON.stringify({ data: "ok" }), { status: 200 }),
    );

    seedRecoveryState(1);

    const apiClient = getAuthenticatedApiClient();
    await (apiClient as any).http.fetch("http://localhost:5001/api/catalog", {
      method: "GET",
    });

    expect(sessionStorage.getItem(RECOVERY_KEY)).toBeNull();
  });

  it("does not reset the recovery counter on a 401 response", async () => {
    mockTokenProvider.mockResolvedValue(null);
    global.fetch = jest.fn().mockResolvedValue(
      new Response("", { status: 401 }),
    );

    seedRecoveryState(1);

    const apiClient = getAuthenticatedApiClient();
    await (apiClient as any).http.fetch("http://localhost:5001/api/catalog", {
      method: "GET",
    });

    const state = sessionStorage.getItem(RECOVERY_KEY);
    expect(state).not.toBeNull();
    expect(JSON.parse(state!).count).toBe(1);
  });
});
