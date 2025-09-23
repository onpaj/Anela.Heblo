import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import App from "../App";
import * as apiClient from "../api/client";
import { InteractionRequiredAuthError } from "@azure/msal-browser";

// Mock the e2eAuth module
jest.mock("../auth/e2eAuth", () => ({
  isE2ETestMode: jest.fn(() => false),
  getE2EAccessToken: jest.fn(() => null),
  useE2EAuth: jest.fn(() => ({
    isAuthenticated: false,
    account: null,
    inProgress: "none" as const,
    login: jest.fn(),
    logout: jest.fn(),
    getAccessToken: jest.fn(),
    getUserInfo: jest.fn(),
    getStoredUserInfo: jest.fn(),
  })),
}));

// Create a mock MSAL instance that we can control
const createMockMsalInstance = () => ({
  initialize: jest.fn().mockResolvedValue(undefined),
  getAllAccounts: jest.fn().mockReturnValue([
    {
      homeAccountId: "test-account-id",
      environment: "login.microsoftonline.com",
      tenantId: "test-tenant-id",
      username: "test@example.com",
      localAccountId: "test-local-account-id",
      name: "Test User",
      idTokenClaims: {},
    },
  ]),
  acquireTokenSilent: jest.fn(),
  loginRedirect: jest.fn().mockResolvedValue(undefined),
  setActiveAccount: jest.fn(),
  getActiveAccount: jest.fn(),
  addEventCallback: jest.fn(),
  removeEventCallback: jest.fn(),
  handleRedirectPromise: jest.fn().mockResolvedValue(null),
});

// Mock PublicClientApplication
jest.mock("@azure/msal-browser", () => ({
  PublicClientApplication: jest.fn(),
  InteractionRequiredAuthError: class extends Error {
    constructor(message: string) {
      super(message);
      this.name = "InteractionRequiredAuthError";
    }
  },
}));

// Spy on API client functions
const setGlobalTokenProviderSpy = jest.spyOn(apiClient, "setGlobalTokenProvider");
const setGlobalAuthRedirectHandlerSpy = jest.spyOn(apiClient, "setGlobalAuthRedirectHandler");

describe("App Component - Authentication Setup", () => {
  let mockMsalInstance: ReturnType<typeof createMockMsalInstance>;
  let originalEnv: typeof process.env;

  beforeEach(() => {
    jest.clearAllMocks();
    
    // Save original env
    originalEnv = process.env;
    
    // Set up test environment variables that loadConfig will read
    process.env = {
      ...originalEnv,
      REACT_APP_API_URL: "http://localhost:5001",
      REACT_APP_USE_MOCK_AUTH: "false",
      REACT_APP_AZURE_CLIENT_ID: "test-client-id",
      REACT_APP_AZURE_AUTHORITY: "https://login.microsoftonline.com/test-tenant-id",
      REACT_APP_AZURE_BACKEND_CLIENT_ID: "8b34be89-f86f-422f-af40-7dbcd30cb66a",
    };
    
    // Clear msalConfig module cache to pick up new environment variables
    delete require.cache[require.resolve("../auth/msalConfig")];
    
    // Clear cached config to ensure fresh load in each test
    const runtimeConfig = require("../config/runtimeConfig");
    (runtimeConfig as any).cachedConfig = null;
    
    mockMsalInstance = createMockMsalInstance();
    
    // Mock successful token acquisition by default
    mockMsalInstance.acquireTokenSilent.mockResolvedValue({
      accessToken: "test-access-token",
      account: mockMsalInstance.getAllAccounts()[0],
    });

    // Setup PublicClientApplication mock
    (require("@azure/msal-browser").PublicClientApplication as jest.Mock)
      .mockImplementation(() => mockMsalInstance);
  });
  
  afterEach(() => {
    // Restore original env
    process.env = originalEnv;
    
    // Clear cached config to ensure fresh load in next test
    const runtimeConfig = require("../config/runtimeConfig");
    (runtimeConfig as any).cachedConfig = null;
    
    // Clear msalConfig module cache to pick up new environment variables
    delete require.cache[require.resolve("../auth/msalConfig")];
    
    // Reset all e2eAuth mocks
    const e2eAuth = require("../auth/e2eAuth");
    jest.mocked(e2eAuth.isE2ETestMode).mockReturnValue(false);
    jest.mocked(e2eAuth.getE2EAccessToken).mockReturnValue(null);
    jest.mocked(e2eAuth.useE2EAuth).mockReturnValue({
      isAuthenticated: false,
      account: null,
      inProgress: "none" as const,
      login: jest.fn(),
      logout: jest.fn(),
      getAccessToken: jest.fn(),
      getUserInfo: jest.fn(),
      getStoredUserInfo: jest.fn(),
    });
  });

  describe("Global Token Provider Setup", () => {
    it("should set up global token provider for real authentication", async () => {
      render(<App />);

      await waitFor(() => {
        expect(setGlobalTokenProviderSpy).toHaveBeenCalled();
      });

      // Get the token provider function that was passed
      const tokenProviderCall = setGlobalTokenProviderSpy.mock.calls[0];
      expect(tokenProviderCall).toBeDefined();
      
      const tokenProvider = tokenProviderCall[0];
      expect(typeof tokenProvider).toBe("function");

      // Test the token provider
      const token = await tokenProvider();
      expect(token).toBe("test-access-token");
      expect(mockMsalInstance.acquireTokenSilent).toHaveBeenCalledWith({
        scopes: ["api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"],
        prompt: "consent",
        account: mockMsalInstance.getAllAccounts()[0],
        forceRefresh: false,
      });
    });

    it("should support force refresh in token provider", async () => {
      render(<App />);

      await waitFor(() => {
        expect(setGlobalTokenProviderSpy).toHaveBeenCalled();
      });

      const tokenProvider = setGlobalTokenProviderSpy.mock.calls[0][0];
      
      // Test force refresh
      await tokenProvider(true);
      
      expect(mockMsalInstance.acquireTokenSilent).toHaveBeenCalledWith({
        scopes: ["api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"],
        prompt: "consent",
        account: mockMsalInstance.getAllAccounts()[0],
        forceRefresh: true,
      });
    });

    it("should handle InteractionRequiredAuthError in token provider", async () => {
      const interactionError = new InteractionRequiredAuthError("Interaction required");
      mockMsalInstance.acquireTokenSilent.mockRejectedValue(interactionError);

      render(<App />);

      await waitFor(() => {
        expect(setGlobalTokenProviderSpy).toHaveBeenCalled();
      });

      const tokenProvider = setGlobalTokenProviderSpy.mock.calls[0][0];
      
      const token = await tokenProvider();
      expect(token).toBeNull();
    });

    it("should handle no accounts scenario", async () => {
      mockMsalInstance.getAllAccounts.mockReturnValue([]);

      render(<App />);

      await waitFor(() => {
        expect(setGlobalTokenProviderSpy).toHaveBeenCalled();
      });

      const tokenProvider = setGlobalTokenProviderSpy.mock.calls[0][0];
      
      const token = await tokenProvider();
      expect(token).toBeNull();
      expect(mockMsalInstance.acquireTokenSilent).not.toHaveBeenCalled();
    });
  });

  describe("Global Auth Redirect Handler Setup", () => {
    it("should set up global auth redirect handler", async () => {
      render(<App />);

      await waitFor(() => {
        expect(setGlobalAuthRedirectHandlerSpy).toHaveBeenCalled();
      });

      const redirectHandlerCall = setGlobalAuthRedirectHandlerSpy.mock.calls[0];
      expect(redirectHandlerCall).toBeDefined();
      
      const redirectHandler = redirectHandlerCall[0];
      expect(typeof redirectHandler).toBe("function");
    });

    it("should execute login redirect when redirect handler is called", async () => {
      // Mock sessionStorage.clear properly for Jest environment
      const originalClear = window.sessionStorage.clear;
      const clearSpy = jest.fn();
      window.sessionStorage.clear = clearSpy;

      render(<App />);

      await waitFor(() => {
        expect(setGlobalAuthRedirectHandlerSpy).toHaveBeenCalled();
      });

      const redirectHandler = setGlobalAuthRedirectHandlerSpy.mock.calls[0][0];
      
      // Execute the redirect handler
      await redirectHandler();

      expect(clearSpy).toHaveBeenCalled();
      expect(mockMsalInstance.loginRedirect).toHaveBeenCalledWith({
        scopes: ["api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"],
        prompt: "select_account",
      });

      // Restore original clear method
      window.sessionStorage.clear = originalClear;
    });

    it("should handle login redirect failure with fallback", async () => {
      mockMsalInstance.loginRedirect.mockRejectedValue(new Error("Redirect failed"));

      // Mock window.location.href setter
      Object.defineProperty(window, "location", {
        value: {
          ...window.location,
          href: "",
        },
        writable: true,
      });

      render(<App />);

      await waitFor(() => {
        expect(setGlobalAuthRedirectHandlerSpy).toHaveBeenCalled();
      });

      const redirectHandler = setGlobalAuthRedirectHandlerSpy.mock.calls[0][0];
      
      // Execute the redirect handler
      await redirectHandler();

      expect(mockMsalInstance.loginRedirect).toHaveBeenCalled();
      expect(window.location.href).toBe("/");
    });
  });



});