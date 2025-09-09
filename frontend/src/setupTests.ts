// jest-dom adds custom jest matchers for asserting on DOM nodes.
// allows you to do things like:
// expect(element).toHaveTextContent(/react/i)
// learn more: https://github.com/testing-library/jest-dom
import "@testing-library/jest-dom";
import React from "react";

// Mock crypto for MSAL
Object.defineProperty(global, "crypto", {
  value: {
    getRandomValues: (arr: any) => {
      for (let i = 0; i < arr.length; i++) {
        arr[i] = Math.floor(Math.random() * 256);
      }
      return arr;
    },
    subtle: {
      digest: jest.fn().mockResolvedValue(new ArrayBuffer(32)),
      generateKey: jest.fn().mockResolvedValue({}),
      importKey: jest.fn().mockResolvedValue({}),
      sign: jest.fn().mockResolvedValue(new ArrayBuffer(32)),
      verify: jest.fn().mockResolvedValue(true),
    },
  },
});

// Mock MSAL
jest.mock("@azure/msal-browser", () => ({
  PublicClientApplication: jest.fn().mockImplementation(() => ({
    initialize: jest.fn().mockResolvedValue(undefined),
    acquireTokenSilent: jest.fn().mockResolvedValue({
      accessToken: "mock-token",
      account: {
        homeAccountId: "mock-account-id",
        environment: "mock-environment",
        tenantId: "mock-tenant-id",
        username: "test@example.com",
        localAccountId: "mock-local-account-id",
        name: "Test User",
        idTokenClaims: {},
      },
    }),
    acquireTokenPopup: jest.fn().mockResolvedValue({
      accessToken: "mock-token",
    }),
    loginPopup: jest.fn().mockResolvedValue({
      account: {
        homeAccountId: "mock-account-id",
        environment: "mock-environment",
        tenantId: "mock-tenant-id",
        username: "test@example.com",
        localAccountId: "mock-local-account-id",
        name: "Test User",
        idTokenClaims: {},
      },
    }),
    logout: jest.fn().mockResolvedValue(undefined),
    getAllAccounts: jest.fn().mockReturnValue([]),
    getAccountByHomeId: jest.fn().mockReturnValue(null),
    getAccountByLocalId: jest.fn().mockReturnValue(null),
    getAccountByUsername: jest.fn().mockReturnValue(null),
    setActiveAccount: jest.fn(),
    getActiveAccount: jest.fn().mockReturnValue(null),
    addEventCallback: jest.fn(),
    removeEventCallback: jest.fn(),
    enableAccountStorageEvents: jest.fn(),
    disableAccountStorageEvents: jest.fn(),
    handleRedirectPromise: jest.fn().mockResolvedValue(null),
    loginRedirect: jest.fn().mockResolvedValue({
      account: {
        homeAccountId: "mock-account-id",
        environment: "mock-environment",
        tenantId: "mock-tenant-id",
        username: "test@example.com",
        localAccountId: "mock-local-account-id",
        name: "Test User",
        idTokenClaims: {},
      },
    }),
    acquireTokenRedirect: jest.fn().mockResolvedValue({
      accessToken: "mock-token",
    }),
    logoutRedirect: jest.fn().mockResolvedValue(undefined),
    logoutPopup: jest.fn().mockResolvedValue(undefined),
  })),
  EventType: {
    LOGIN_SUCCESS: "msal:loginSuccess",
    LOGIN_FAILURE: "msal:loginFailure",
    ACQUIRE_TOKEN_SUCCESS: "msal:acquireTokenSuccess",
    ACQUIRE_TOKEN_FAILURE: "msal:acquireTokenFailure",
    LOGOUT_SUCCESS: "msal:logoutSuccess",
  },
  BrowserAuthError: class extends Error {
    constructor(message: string) {
      super(message);
      this.name = "BrowserAuthError";
    }
  },
  InteractionRequiredAuthError: class extends Error {
    constructor(message: string) {
      super(message);
      this.name = "InteractionRequiredAuthError";
    }
  },
}));

// Mock MSAL React
jest.mock("@azure/msal-react", () => ({
  MsalProvider: ({ children }: { children: React.ReactNode }) => children,
  useMsal: () => ({
    instance: {
      initialize: jest.fn().mockResolvedValue(undefined),
      acquireTokenSilent: jest
        .fn()
        .mockResolvedValue({ accessToken: "mock-token" }),
      loginPopup: jest
        .fn()
        .mockResolvedValue({ account: { username: "test@example.com" } }),
      logout: jest.fn().mockResolvedValue(undefined),
      getAllAccounts: jest.fn().mockReturnValue([]),
      getActiveAccount: jest.fn().mockReturnValue(null),
    },
    accounts: [],
    inProgress: "none",
  }),
  useAccount: () => null,
  useIsAuthenticated: () => false,
  useMsalAuthentication: () => ({
    login: jest.fn(),
    result: null,
    error: null,
  }),
  AuthenticatedTemplate: ({ children }: { children: React.ReactNode }) => null,
  UnauthenticatedTemplate: ({ children }: { children: React.ReactNode }) =>
    children,
}));

// Mock window.location
Object.defineProperty(window, "location", {
  value: {
    href: "http://localhost:3000",
    origin: "http://localhost:3000",
    protocol: "http:",
    host: "localhost:3000",
    hostname: "localhost",
    port: "3000",
    pathname: "/",
    search: "",
    hash: "",
    assign: jest.fn(),
    reload: jest.fn(),
    replace: jest.fn(),
  },
});

// Mock sessionStorage and localStorage
const storageMock = () => {
  let storage: { [key: string]: string } = {};
  return {
    getItem: (key: string) => storage[key] || null,
    setItem: (key: string, value: string) => {
      storage[key] = value;
    },
    removeItem: (key: string) => {
      delete storage[key];
    },
    clear: () => {
      storage = {};
    },
    length: Object.keys(storage).length,
    key: (index: number) => Object.keys(storage)[index] || null,
  };
};

Object.defineProperty(window, "sessionStorage", { value: storageMock() });
Object.defineProperty(window, "localStorage", { value: storageMock() });

// Mock fetch for runtime configuration
global.fetch = jest.fn(() =>
  Promise.resolve({
    ok: true,
    status: 200,
    json: () =>
      Promise.resolve({
        apiUrl: "http://localhost:8080",
        useMockAuth: true,
        azureClientId: "mock-client-id",
        azureAuthority: "https://login.microsoftonline.com/mock-tenant-id",
      }),
  }),
) as jest.Mock;

// Suppress ReactDOMTestUtils.act deprecation warnings and test error messages in console
const originalError = console.error;
beforeEach(() => {
  console.error = (...args: any[]) => {
    if (
      typeof args[0] === "string" &&
      (args[0].includes("ReactDOMTestUtils.act is deprecated") ||
        args[0].includes("Warning: `ReactDOMTestUtils.act` is deprecated") ||
        args[0].includes("Error changing to InReserve state:"))
    ) {
      return;
    }
    originalError.call(console, ...args);
  };
});

afterEach(() => {
  console.error = originalError;
});
