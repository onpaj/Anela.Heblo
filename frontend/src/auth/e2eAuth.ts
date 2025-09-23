import { useState, useEffect } from "react";
import { UserInfo } from "./useAuth";
import { StoredUserInfo } from "./userStorage";

/**
 * E2E Authentication for UI tests
 * Detects when running in E2E test mode and uses E2E session cookies
 */


// Mock user for E2E tests
const E2E_USER: UserInfo = {
  name: "E2E Test User",
  email: "e2e@anela-heblo.com",
  initials: "E2E",
  roles: ["finance_reader"],
};

/**
 * Check if we're running in E2E test mode
 * Look for E2E authentication cookies or persistent E2E flag
 */
export const isE2ETestMode = (): boolean => {
  // Check for E2E session cookie
  const cookies = document.cookie;
  const hasE2EAuth =
    cookies.includes("E2ETestSession=") ||
    cookies.includes("e2e-auth=") ||
    cookies.includes("E2ETestAuth=");

  // Check if we're accessing the app via the E2E endpoint or parameter
  const isE2EUrl =
    window.location.href.includes("/api/e2etest/app") ||
    window.location.search.includes("e2e=true");

  // Check persistent E2E mode flag in sessionStorage
  const hasE2EFlag = sessionStorage.getItem("e2e-mode") === "true";

  // Set persistent flag if we detect E2E mode
  if (isE2EUrl && !hasE2EFlag) {
    sessionStorage.setItem("e2e-mode", "true");
    console.log("🧪 E2E mode detected - setting persistent flag");
  }

  const isE2EMode = hasE2EAuth || isE2EUrl || hasE2EFlag;

  console.log("🧪 E2E Test Mode Detection:", {
    hasE2EAuth,
    isE2EUrl,
    hasE2EFlag,
    isE2EMode,
    cookies: cookies ? "[PRESENT]" : "[EMPTY]",
    url: window.location.href,
  });

  return isE2EMode;
};

/**
 * Get E2E access token from sessionStorage
 * This should be set by the E2E test initialization
 */
export const getE2EAccessToken = (): string | null => {
  // Check if we have an E2E test token stored by the test
  const e2eToken = sessionStorage.getItem("e2e-test-token");
  if (e2eToken) {
    console.log(
      "🎫 E2E auth using Service Principal token from sessionStorage",
    );
    return e2eToken;
  }

  console.log(
    "🎫 E2E auth: No token found in sessionStorage, using session cookies (no Authorization header)",
  );
  return null;
};

/**
 * E2E Authentication Hook
 * Provides authentication interface compatible with MSAL but uses E2E session
 */
export const useE2EAuth = () => {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(false);
  const [storedUserInfo, setStoredUserInfo] = useState<StoredUserInfo | null>(
    null,
  );
  const [isInitialized, setIsInitialized] = useState<boolean>(false);

  useEffect(() => {
    const initializeE2EAuth = async () => {
      console.log("🧪 Initializing E2E authentication...");

      // Check if we have E2E session
      if (isE2ETestMode()) {
        console.log(
          "✅ E2E test mode detected - auto-authenticating with test user",
        );

        setIsAuthenticated(true);
        const mockStoredUser: StoredUserInfo = {
          ...E2E_USER,
          lastLogin: new Date().toISOString(),
        };
        setStoredUserInfo(mockStoredUser);
      } else {
        console.log("❌ E2E test mode not detected");
        setIsAuthenticated(false);
        setStoredUserInfo(null);
      }

      setIsInitialized(true);
    };

    initializeE2EAuth();
  }, []);

  const login = async () => {
    console.log("🧪 E2E login called - should already be authenticated");
    // In E2E mode, we should already be authenticated
    // This is a no-op since E2E auth is automatic
    return Promise.resolve();
  };

  const logout = async () => {
    console.log("🧪 E2E logout called");
    setIsAuthenticated(false);
    setStoredUserInfo(null);
  };

  const getAccessToken = async (): Promise<string | null> => {
    if (!isAuthenticated) {
      console.warn("🧪 E2E getAccessToken called but not authenticated");
      return null;
    }
    return getE2EAccessToken();
  };

  const getUserInfo = (): UserInfo | null => {
    return isAuthenticated ? E2E_USER : null;
  };

  const getStoredUserInfo = (): StoredUserInfo | null => {
    return storedUserInfo;
  };

  return {
    isAuthenticated,
    account: isAuthenticated
      ? {
          name: E2E_USER.name,
          username: E2E_USER.email,
          localAccountId: "e2e-account-id",
          homeAccountId: "e2e-home-account-id",
          environment: "e2e-environment",
          tenantId: "e2e-tenant-id",
          idTokenClaims: { roles: E2E_USER.roles },
        }
      : null,
    inProgress: isInitialized ? ("none" as const) : ("loading" as const),
    login,
    logout,
    getAccessToken,
    getUserInfo,
    getStoredUserInfo,
  };
};
