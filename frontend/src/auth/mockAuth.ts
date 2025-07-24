import { useState, useEffect } from 'react';
import { UserInfo } from './useAuth';
import { StoredUserInfo, UserStorage } from './userStorage';

/**
 * Mock authentication for development testing
 * This simulates a successful login without using real credentials
 */
export const createMockUser = (): UserInfo => {
  return {
    name: 'Mock User',
    email: 'mock.user@example.com',
    initials: 'MU',
    roles: ['admin'],
  };
};

/**
 * Check if we're in development mode and should use mock auth
 */
export const shouldUseMockAuth = (): boolean => {
  return process.env.NODE_ENV === 'development' && 
         process.env.REACT_APP_USE_MOCK_AUTH === 'true';
};

/**
 * Simulate login delay for realistic testing
 */
export const mockLoginDelay = (): Promise<void> => {
  return new Promise(resolve => setTimeout(resolve, 1000));
};

// Mock user data for development
const MOCK_USER: UserInfo = createMockUser();

export const useMockAuth = () => {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(false);
  const [storedUserInfo, setStoredUserInfo] = useState<StoredUserInfo | null>(null);

  // Auto-authenticate in development
  useEffect(() => {
    // Simulate authentication delay
    const timer = setTimeout(() => {
      setIsAuthenticated(true);
      
      // Store mock user info
      const mockStoredUser: StoredUserInfo = {
        ...MOCK_USER,
        lastLogin: new Date().toISOString()
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
      lastLogin: new Date().toISOString()
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
    return isAuthenticated ? 'mock-access-token' : null;
  };

  const getUserInfo = (): UserInfo | null => {
    return isAuthenticated ? MOCK_USER : null;
  };

  const getStoredUserInfo = (): StoredUserInfo | null => {
    return storedUserInfo;
  };

  return {
    isAuthenticated,
    account: isAuthenticated ? { 
      name: MOCK_USER.name, 
      username: MOCK_USER.email,
      localAccountId: 'mock-account-id',
      homeAccountId: 'mock-home-account-id',
      environment: 'mock-environment',
      tenantId: 'mock-tenant-id',
      idTokenClaims: { roles: MOCK_USER.roles }
    } : null,
    inProgress: 'none' as const,
    login,
    logout,
    getAccessToken,
    getUserInfo,
    getStoredUserInfo,
  };
};