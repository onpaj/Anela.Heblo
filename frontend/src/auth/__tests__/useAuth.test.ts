import { renderHook, act } from "@testing-library/react";
import { useAuth } from "../useAuth";
import { InteractionRequiredAuthError } from "@azure/msal-browser";
import * as apiClient from "../../api/client";

// Mock the dependencies
jest.mock("../../api/client", () => ({
  clearTokenCache: jest.fn(),
}));

jest.mock("../userStorage", () => ({
  UserStorage: {
    getUserInfo: jest.fn().mockReturnValue(null),
    setUserInfo: jest.fn(),
    clearUserInfo: jest.fn(),
  },
}));

jest.mock("../msalConfig", () => ({
  apiRequest: {
    scopes: ["api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"],
    prompt: "consent",
  },
  loginRequest: {
    scopes: ["User.Read", "openid", "profile"],
    prompt: "select_account",
  },
  loginRedirectRequest: {
    scopes: ["User.Read", "openid", "profile"],
    prompt: "select_account",
  },
}));

// Mock useMsal hook with a factory function
const mockUseMsal = jest.fn();
jest.mock("@azure/msal-react", () => ({
  useMsal: () => mockUseMsal(),
}));

// Mock MSAL instance and account
const mockInstance = {
  acquireTokenSilent: jest.fn(),
  loginRedirect: jest.fn(),
  loginPopup: jest.fn(),
  logoutRedirect: jest.fn(),
};

const mockAccount = {
  homeAccountId: "test-account-id",
  environment: "login.microsoftonline.com",
  tenantId: "test-tenant-id",
  username: "test@example.com",
  localAccountId: "test-local-account-id",
  name: "Test User",
  idTokenClaims: {
    roles: ["admin"],
  },
};

describe("useAuth - Enhanced Token Refresh", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    
    // Reset to default successful state
    mockUseMsal.mockReturnValue({
      instance: mockInstance,
      accounts: [mockAccount],
      inProgress: "none",
    });
    
    // Reset to successful token acquisition by default
    mockInstance.acquireTokenSilent.mockResolvedValue({
      accessToken: "mock-access-token",
      account: mockAccount,
    });
  });

  describe("getAccessToken", () => {
    it("should successfully acquire token silently", async () => {
      const { result } = renderHook(() => useAuth());

      const token = await act(async () => {
        return await result.current.getAccessToken();
      });

      expect(token).toBe("mock-access-token");
      expect(mockInstance.acquireTokenSilent).toHaveBeenCalledWith({
        scopes: ["api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"],
        prompt: "consent",
        account: mockAccount,
        forceRefresh: false,
      });
    });

    it("should support force refresh parameter", async () => {
      const { result } = renderHook(() => useAuth());

      await act(async () => {
        await result.current.getAccessToken(true);
      });

      expect(mockInstance.acquireTokenSilent).toHaveBeenCalledWith({
        scopes: ["api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default"],
        prompt: "consent",
        account: mockAccount,
        forceRefresh: true,
      });
    });

    it("should return null when no account is available", async () => {
      // Mock useMsal to return no accounts
      mockUseMsal.mockReturnValue({
        instance: mockInstance,
        accounts: [],
        inProgress: "none",
      });

      const { result } = renderHook(() => useAuth());

      const token = await act(async () => {
        return await result.current.getAccessToken();
      });

      expect(token).toBeNull();
      expect(mockInstance.acquireTokenSilent).not.toHaveBeenCalled();
    });

    it("should handle InteractionRequiredAuthError with redirect", async () => {
      const interactionError = new InteractionRequiredAuthError("Interaction required");
      mockInstance.acquireTokenSilent.mockRejectedValue(interactionError);
      mockInstance.loginRedirect.mockResolvedValue(undefined);

      const { result } = renderHook(() => useAuth());

      const token = await act(async () => {
        return await result.current.getAccessToken();
      });

      expect(token).toBeNull();
      expect(apiClient.clearTokenCache).toHaveBeenCalled();
      expect(mockInstance.loginRedirect).toHaveBeenCalledWith({
        scopes: ["User.Read", "openid", "profile"],
        prompt: "select_account",
      });
    });

    it("should fallback to popup when redirect fails", async () => {
      const interactionError = new InteractionRequiredAuthError("Interaction required");
      const redirectError = new Error("Redirect failed");
      
      mockInstance.acquireTokenSilent.mockRejectedValue(interactionError);
      mockInstance.loginRedirect.mockRejectedValue(redirectError);
      mockInstance.loginPopup.mockResolvedValue({
        account: mockAccount,
      });
      
      // Mock successful token acquisition after popup login
      mockInstance.acquireTokenSilent
        .mockRejectedValueOnce(interactionError) // First call fails
        .mockResolvedValue({ // Second call succeeds after popup login
          accessToken: "new-access-token",
          account: mockAccount,
        });

      const { result } = renderHook(() => useAuth());

      const token = await act(async () => {
        return await result.current.getAccessToken();
      });

      expect(mockInstance.loginRedirect).toHaveBeenCalled();
      expect(mockInstance.loginPopup).toHaveBeenCalledWith({
        scopes: ["User.Read", "openid", "profile"],
        prompt: "select_account",
      });
      expect(token).toBe("new-access-token");
    });

    it("should return null when both redirect and popup fail", async () => {
      const interactionError = new InteractionRequiredAuthError("Interaction required");
      const redirectError = new Error("Redirect failed");
      const popupError = new Error("Popup failed");
      
      mockInstance.acquireTokenSilent.mockRejectedValue(interactionError);
      mockInstance.loginRedirect.mockRejectedValue(redirectError);
      mockInstance.loginPopup.mockRejectedValue(popupError);

      const { result } = renderHook(() => useAuth());

      const token = await act(async () => {
        return await result.current.getAccessToken();
      });

      expect(token).toBeNull();
      expect(mockInstance.loginRedirect).toHaveBeenCalled();
      expect(mockInstance.loginPopup).toHaveBeenCalled();
    });

    it("should handle non-interaction auth errors gracefully", async () => {
      const genericError = new Error("Network error");
      mockInstance.acquireTokenSilent.mockRejectedValue(genericError);

      const { result } = renderHook(() => useAuth());

      const token = await act(async () => {
        return await result.current.getAccessToken();
      });

      expect(token).toBeNull();
      expect(mockInstance.loginRedirect).not.toHaveBeenCalled();
      expect(mockInstance.loginPopup).not.toHaveBeenCalled();
    });
  });

  describe("login", () => {
    it("should use redirect flow first", async () => {
      mockInstance.loginRedirect.mockResolvedValue(undefined);

      const { result } = renderHook(() => useAuth());

      await act(async () => {
        await result.current.login();
      });

      expect(mockInstance.loginRedirect).toHaveBeenCalledWith({
        scopes: ["User.Read", "openid", "profile"],
        prompt: "select_account",
      });
      expect(mockInstance.loginPopup).not.toHaveBeenCalled();
    });

    it("should fallback to popup when redirect fails", async () => {
      const redirectError = new Error("Redirect failed");
      mockInstance.loginRedirect.mockRejectedValue(redirectError);
      mockInstance.loginPopup.mockResolvedValue({
        account: mockAccount,
      });

      const { result } = renderHook(() => useAuth());

      await act(async () => {
        await result.current.login();
      });

      expect(mockInstance.loginRedirect).toHaveBeenCalled();
      expect(mockInstance.loginPopup).toHaveBeenCalledWith({
        scopes: ["User.Read", "openid", "profile"],
        prompt: "select_account",
      });
    });
  });

  describe("logout", () => {
    it("should clear cache and redirect logout", async () => {
      mockInstance.logoutRedirect.mockResolvedValue(undefined);

      const { result } = renderHook(() => useAuth());

      await act(async () => {
        await result.current.logout();
      });

      expect(apiClient.clearTokenCache).toHaveBeenCalled();
      expect(mockInstance.logoutRedirect).toHaveBeenCalledWith({
        account: mockAccount,
        postLogoutRedirectUri: "http://localhost:3000",
      });
    });
  });

  describe("user info extraction", () => {
    it("should extract user info from account", () => {
      const { result } = renderHook(() => useAuth());

      const userInfo = result.current.getUserInfo();

      expect(userInfo).toEqual({
        name: "Test User",
        email: "test@example.com",
        initials: "TU",
        roles: ["admin"],
      });
    });

    it("should generate initials from name", () => {
      const accountWithLongName = {
        ...mockAccount,
        name: "John Michael Smith",
      };

      mockUseMsal.mockReturnValue({
        instance: mockInstance,
        accounts: [accountWithLongName],
        inProgress: "none",
      });

      const { result } = renderHook(() => useAuth());

      const userInfo = result.current.getUserInfo();

      expect(userInfo?.initials).toBe("JM"); // Only first two initials
    });

    it("should handle missing name gracefully", () => {
      const accountWithoutName = {
        ...mockAccount,
        name: undefined,
        username: "test@example.com",
      };

      mockUseMsal.mockReturnValue({
        instance: mockInstance,
        accounts: [accountWithoutName],
        inProgress: "none",
      });

      const { result } = renderHook(() => useAuth());

      const userInfo = result.current.getUserInfo();

      expect(userInfo?.name).toBe("test@example.com");
      expect(userInfo?.initials).toBe("T"); // First letter of email
    });
  });
});