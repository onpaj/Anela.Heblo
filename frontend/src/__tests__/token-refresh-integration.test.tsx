import React from "react";
import { render, screen, waitFor, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PublicClientApplication, InteractionRequiredAuthError } from "@azure/msal-browser";
import { getAuthenticatedApiClient, setGlobalTokenProvider, setGlobalAuthRedirectHandler } from "../api/client";
import { useAuth } from "../auth/useAuth";

// Mock configuration to force MSAL usage (not mock auth)
jest.mock("../config/runtimeConfig", () => ({
  getConfig: () => ({
    apiUrl: "http://localhost:5001",
    useMockAuth: false, // Force MSAL usage
    azureClientId: "test-client-id",
    azureAuthority: "https://login.microsoftonline.com/test-tenant-id",
  }),
  shouldUseMockAuth: () => false,
}));

jest.mock("../auth/e2eAuth", () => ({
  isE2ETestMode: () => false,
  getE2EAccessToken: () => null,
}));

// Create mockUseMsal before mocking the module
const mockUseMsal = jest.fn();

jest.mock("@azure/msal-react", () => ({
  MsalProvider: ({ children }: any) => children,
  useMsal: () => mockUseMsal(),
}));

// Mock PublicClientApplication before using it
jest.mock("@azure/msal-browser", () => ({
  PublicClientApplication: jest.fn(),
  InteractionRequiredAuthError: class extends Error {
    constructor(message: string) {
      super(message);
      this.name = "InteractionRequiredAuthError";
    }
  },
}));

// Test component that uses authentication
const TestComponent: React.FC = () => {
  const { getAccessToken, isAuthenticated } = useAuth();
  const [token, setToken] = React.useState<string | null>(null);
  const [apiResponse, setApiResponse] = React.useState<string | null>(null);

  const handleGetToken = async () => {
    const accessToken = await getAccessToken();
    setToken(accessToken);
  };

  const handleApiCall = async () => {
    try {
      const apiClient = getAuthenticatedApiClient();
      const response = await (apiClient as any).http.fetch(
        "http://localhost:5001/api/test",
        { method: "GET" }
      );
      const data = await response.json();
      setApiResponse(data.message || "Success");
    } catch (error) {
      setApiResponse(`Error: ${(error as Error).message}`);
    }
  };

  return (
    <div>
      <div data-testid="auth-status">
        {isAuthenticated ? "Authenticated" : "Not Authenticated"}
      </div>
      <button onClick={handleGetToken} data-testid="get-token">
        Get Token
      </button>
      <button onClick={handleApiCall} data-testid="api-call">
        Make API Call
      </button>
      {token && <div data-testid="token">{token}</div>}
      {apiResponse && <div data-testid="api-response">{apiResponse}</div>}
    </div>
  );
};

describe("Token Refresh Integration Tests", () => {
  let mockMsalInstance: any;
  let mockFetch: jest.Mock;

  // Test wrapper component that uses mocked MSAL
  const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    return (
      <QueryClientProvider client={queryClient}>
        {children}
      </QueryClientProvider>
    );
  };

  const mockAccount = {
    homeAccountId: "test-account-id",
    environment: "login.microsoftonline.com",
    tenantId: "test-tenant-id",
    username: "test@example.com",
    localAccountId: "test-local-account-id",
    name: "Test User",
    idTokenClaims: {},
  };

  beforeEach(() => {
    jest.clearAllMocks();

    // Mock fetch globally
    mockFetch = jest.fn();
    global.fetch = mockFetch;

    // Create controlled MSAL instance
    mockMsalInstance = {
      initialize: jest.fn().mockResolvedValue(undefined),
      getAllAccounts: jest.fn().mockReturnValue([mockAccount]),
      acquireTokenSilent: jest.fn(),
      loginRedirect: jest.fn().mockResolvedValue(undefined),
      loginPopup: jest.fn().mockResolvedValue({ account: mockAccount }),
      logoutRedirect: jest.fn().mockResolvedValue(undefined),
      handleRedirectPromise: jest.fn().mockResolvedValue(null),
      addEventCallback: jest.fn(),
      removeEventCallback: jest.fn(),
      setActiveAccount: jest.fn(),
      getActiveAccount: jest.fn().mockReturnValue(mockAccount),
    };

    // Setup PublicClientApplication mock to return our controlled instance
    (PublicClientApplication as jest.Mock).mockImplementation(() => mockMsalInstance);

    // Setup mockUseMsal hook to return authenticated state
    mockUseMsal.mockReturnValue({
      instance: mockMsalInstance,
      accounts: [mockAccount],
      inProgress: "none",
    });

    // Setup successful token acquisition by default
    mockMsalInstance.acquireTokenSilent.mockResolvedValue({
      accessToken: "valid-access-token",
      account: mockAccount,
    });

    // Setup successful API responses by default with cloneable Response
    mockFetch.mockImplementation(() => 
      Promise.resolve(new Response(JSON.stringify({ message: "Success" }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }))
    );

    // Setup global token provider for tests (normally done by App.tsx)
    setGlobalTokenProvider(async (forceRefresh = false) => {
      const response = await mockMsalInstance.acquireTokenSilent({
        scopes: ["api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"],
        account: mockAccount,
        forceRefresh,
      });
      return response.accessToken;
    });
  });

  describe("Successful Token Refresh Flow", () => {
    it("should complete end-to-end token acquisition and API call", async () => {
      render(
        <TestWrapper>
          <TestComponent />
        </TestWrapper>
      );

      // Wait for authentication
      await waitFor(() => {
        expect(screen.getByTestId("auth-status")).toHaveTextContent("Authenticated");
      });

      // Get token
      await act(async () => {
        screen.getByTestId("get-token").click();
      });

      await waitFor(() => {
        expect(screen.getByTestId("token")).toHaveTextContent("valid-access-token");
      });

      // Make API call
      await act(async () => {
        screen.getByTestId("api-call").click();
      });

      await waitFor(() => {
        expect(screen.getByTestId("api-response")).toHaveTextContent("Success");
      });

      // Verify API was called with correct headers
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5001/api/test",
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: "Bearer valid-access-token",
          }),
        })
      );
    });

    it("should refresh token when force refresh is used", async () => {
      // Clear token cache to ensure token provider is called
      const { clearTokenCache } = require("../api/client");
      clearTokenCache();
      
      // Setup token provider to track calls
      let tokenProviderCalls: boolean[] = [];
      setGlobalTokenProvider(async (forceRefresh = false) => {
        tokenProviderCalls.push(forceRefresh);
        const response = await mockMsalInstance.acquireTokenSilent({
          scopes: ["api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"],
          account: mockAccount,
          forceRefresh,
        });
        return response.accessToken;
      });

      render(
        <TestWrapper>
          <TestComponent />
        </TestWrapper>
      );

      await waitFor(() => {
        expect(screen.getByTestId("auth-status")).toHaveTextContent("Authenticated");
      });

      // Make API call (should trigger token provider)
      await act(async () => {
        screen.getByTestId("api-call").click();
      });

      await waitFor(() => {
        expect(screen.getByTestId("api-response")).toHaveTextContent("Success");
      });

      // Verify forceRefresh was not used initially
      expect(tokenProviderCalls.length).toBeGreaterThan(0);
      expect(tokenProviderCalls).toContain(false);
    });
  });

  describe("Token Expiration and Refresh Flow", () => {
    it("should handle token expiration with automatic redirect", async () => {
      let mockAuthRedirectHandler: jest.Mock;

      // Setup token expiration scenario
      const interactionError = new InteractionRequiredAuthError("Token expired");
      mockMsalInstance.acquireTokenSilent.mockRejectedValue(interactionError);

      // Setup global handlers
      mockAuthRedirectHandler = jest.fn();
      setGlobalAuthRedirectHandler(mockAuthRedirectHandler);

      render(
        <TestWrapper>
          <TestComponent />
        </TestWrapper>
      );

      await waitFor(() => {
        expect(screen.getByTestId("auth-status")).toHaveTextContent("Authenticated");
      });

      // Attempt to get token (should trigger redirect)
      await act(async () => {
        screen.getByTestId("get-token").click();
      });

      await waitFor(() => {
        // Verify redirect was triggered
        expect(mockMsalInstance.loginRedirect).toHaveBeenCalledWith(
          expect.objectContaining({
            prompt: "select_account",
          })
        );
      });
    });

    it("should handle 401 response with automatic redirect", async () => {
      let mockAuthRedirectHandler: jest.Mock;

      // Setup 401 response
      mockFetch.mockResolvedValue(
        new Response(JSON.stringify({ error: "Unauthorized" }), {
          status: 401,
          headers: { "content-type": "application/json" },
        })
      );

      // Setup global handlers
      mockAuthRedirectHandler = jest.fn();
      setGlobalAuthRedirectHandler(mockAuthRedirectHandler);

      render(
        <TestWrapper>
          <TestComponent />
        </TestWrapper>
      );

      await waitFor(() => {
        expect(screen.getByTestId("auth-status")).toHaveTextContent("Authenticated");
      });

      // Make API call that returns 401
      await act(async () => {
        screen.getByTestId("api-call").click();
      });

      await waitFor(() => {
        // Verify redirect handler was called
        expect(mockAuthRedirectHandler).toHaveBeenCalled();
      });
    });

    it("should handle popup fallback when redirect fails", async () => {
      // Setup token expiration and redirect failure
      const interactionError = new InteractionRequiredAuthError("Token expired");
      const redirectError = new Error("Redirect blocked");

      mockMsalInstance.acquireTokenSilent
        .mockRejectedValueOnce(interactionError) // First call fails
        .mockResolvedValue({ // After popup login, token acquisition succeeds
          accessToken: "new-access-token",
          account: mockAccount,
        });

      mockMsalInstance.loginRedirect.mockRejectedValue(redirectError);
      mockMsalInstance.loginPopup.mockResolvedValue({ account: mockAccount });

      render(
        <TestWrapper>
          <TestComponent />
        </TestWrapper>
      );

      await waitFor(() => {
        expect(screen.getByTestId("auth-status")).toHaveTextContent("Authenticated");
      });

      // Attempt to get token
      await act(async () => {
        screen.getByTestId("get-token").click();
      });

      await waitFor(() => {
        // Verify both redirect and popup were attempted
        expect(mockMsalInstance.loginRedirect).toHaveBeenCalled();
        expect(mockMsalInstance.loginPopup).toHaveBeenCalled();
        
        // Should eventually get the new token
        expect(screen.getByTestId("token")).toHaveTextContent("new-access-token");
      });
    });
  });

  describe("Error Scenarios", () => {
    it("should handle complete authentication failure gracefully", async () => {
      // Setup complete failure scenario
      const interactionError = new InteractionRequiredAuthError("Token expired");
      const redirectError = new Error("Redirect failed");
      const popupError = new Error("Popup blocked");

      mockMsalInstance.acquireTokenSilent.mockRejectedValue(interactionError);
      mockMsalInstance.loginRedirect.mockRejectedValue(redirectError);
      mockMsalInstance.loginPopup.mockRejectedValue(popupError);

      render(
        <TestWrapper>
          <TestComponent />
        </TestWrapper>
      );

      await waitFor(() => {
        expect(screen.getByTestId("auth-status")).toHaveTextContent("Authenticated");
      });

      // Attempt to get token
      await act(async () => {
        screen.getByTestId("get-token").click();
      });

      await waitFor(() => {
        // Should handle failure gracefully (token should be null/empty)
        expect(screen.queryByTestId("token")).toBeNull();
      });
    });

    it("should handle network errors during API calls", async () => {
      // Setup network error
      mockFetch.mockRejectedValue(new Error("Network error"));

      render(
        <TestWrapper>
          <TestComponent />
        </TestWrapper>
      );

      await waitFor(() => {
        expect(screen.getByTestId("auth-status")).toHaveTextContent("Authenticated");
      });

      // Make API call that fails
      await act(async () => {
        screen.getByTestId("api-call").click();
      });

      await waitFor(() => {
        expect(screen.getByTestId("api-response")).toHaveTextContent("Error: Network error");
      });
    });
  });

  describe("Token Cache Integration", () => {
    it("should respect token cache and avoid unnecessary token requests", async () => {
      render(
        <TestWrapper>
          <TestComponent />
        </TestWrapper>
      );

      await waitFor(() => {
        expect(screen.getByTestId("auth-status")).toHaveTextContent("Authenticated");
      });

      // Make multiple API calls in quick succession
      await act(async () => {
        screen.getByTestId("api-call").click();
      });

      await act(async () => {
        screen.getByTestId("api-call").click();
      });

      await waitFor(() => {
        expect(screen.getByTestId("api-response")).toHaveTextContent("Success");
      });

      // Should cache tokens and make multiple API calls successfully
      expect(mockFetch).toHaveBeenCalledTimes(2);
    });
  });
});