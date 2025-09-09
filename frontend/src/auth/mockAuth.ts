import { useState, useEffect } from "react";
import { UserInfo } from "./useAuth";
import { StoredUserInfo, UserStorage } from "./userStorage";

// Types for the centralized mock auth service
interface AuthResult {
  success: boolean;
  user?: UserInfo;
  error?: string;
}

interface MockUser extends UserInfo {
  id: string;
}

/**
 * Mock authentication for development testing
 * This simulates a successful login without using real credentials
 */
export const createMockUser = (): MockUser => {
  return {
    id: "mock-user-id",
    name: "Mock User",
    email: "mock@anela-heblo.com",
    initials: "MU",
    roles: ["finance_reader"],
  };
};

/**
 * Check if we should use mock auth
 * Mock auth is used when:
 * 1. REACT_APP_USE_MOCK_AUTH is explicitly set to 'true', OR
 * 2. Required Azure credentials are missing
 */
export const shouldUseMockAuth = (): boolean => {
  // Check runtime config first, fallback to build-time env vars
  try {
    const { getRuntimeConfig } = require("../config/runtimeConfig");
    const config = getRuntimeConfig();
    return (
      config.useMockAuth || !config.azureClientId || !config.azureAuthority
    );
  } catch {
    // Fallback to build-time environment variables if runtime config not available
    return (
      process.env.REACT_APP_USE_MOCK_AUTH === "true" ||
      !process.env.REACT_APP_AZURE_CLIENT_ID ||
      !process.env.REACT_APP_AZURE_AUTHORITY
    );
  }
};

/**
 * Simulate login delay for realistic testing
 */
export const mockLoginDelay = (): Promise<void> => {
  return new Promise((resolve) => setTimeout(resolve, 1000));
};

// Mock user data for development
const MOCK_USER: MockUser = createMockUser();

/**
 * Centralized Mock Authentication Service
 * According to documentation specification (Authentication.md section 7.1.2)
 */
export const mockAuthService = {
  /**
   * Mock login - simulates successful authentication
   */
  login: async (): Promise<AuthResult> => {
    try {
      await mockLoginDelay();

      UserStorage.setUserInfo(MOCK_USER);

      return {
        success: true,
        user: MOCK_USER,
      };
    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : "Login failed",
      };
    }
  },

  /**
   * Mock logout - clears user data
   */
  logout: (): void => {
    UserStorage.clearUserInfo();
  },

  /**
   * Get current mock user
   */
  getUser: (): MockUser | null => {
    const stored = UserStorage.getUserInfo();
    return stored ? MOCK_USER : null;
  },

  /**
   * Check if user is authenticated (has stored user info)
   */
  isAuthenticated: (): boolean => {
    const stored = UserStorage.getUserInfo();
    return stored !== null;
  },

  /**
   * Get fake Bearer token for API calls
   * Returns consistent token for mock authentication
   */
  getAccessToken: (): string => {
    const token = "mock-bearer-token";
    console.log("🎭 Mock auth service providing fake token for API call");
    return token;
  },
};

export const useMockAuth = () => {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(false);
  const [storedUserInfo, setStoredUserInfo] = useState<StoredUserInfo | null>(
    null,
  );

  // Auto-authenticate in development
  useEffect(() => {
    // Simulate authentication delay
    const timer = setTimeout(() => {
      setIsAuthenticated(true);

      // Store mock user info
      const mockStoredUser: StoredUserInfo = {
        ...MOCK_USER,
        lastLogin: new Date().toISOString(),
      };

      UserStorage.setUserInfo(MOCK_USER);
      setStoredUserInfo(mockStoredUser);
    }, 100); // Short delay to simulate auth check

    return () => clearTimeout(timer);
  }, []);

  const login = async () => {
    // Mock login - just set authenticated state
    setIsAuthenticated(true);

    const mockStoredUser: StoredUserInfo = {
      ...MOCK_USER,
      lastLogin: new Date().toISOString(),
    };

    UserStorage.setUserInfo(MOCK_USER);
    setStoredUserInfo(mockStoredUser);
  };

  const logout = async () => {
    setIsAuthenticated(false);
    UserStorage.clearUserInfo();
    setStoredUserInfo(null);
  };

  const getAccessToken = async (): Promise<string | null> => {
    // Return mock token
    return isAuthenticated ? "mock-access-token" : null;
  };

  const getUserInfo = (): UserInfo | null => {
    return isAuthenticated ? MOCK_USER : null;
  };

  const getStoredUserInfo = (): StoredUserInfo | null => {
    return storedUserInfo;
  };

  return {
    isAuthenticated,
    account: isAuthenticated
      ? {
          name: MOCK_USER.name,
          username: MOCK_USER.email,
          localAccountId: "mock-account-id",
          homeAccountId: "mock-home-account-id",
          environment: "mock-environment",
          tenantId: "mock-tenant-id",
          idTokenClaims: { roles: MOCK_USER.roles },
        }
      : null,
    inProgress: "none" as const,
    login,
    logout,
    getAccessToken,
    getUserInfo,
    getStoredUserInfo,
  };
};
