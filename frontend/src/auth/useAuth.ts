import { useMsal } from '@azure/msal-react';
import { loginRequest, loginRedirectRequest, apiRequest } from './msalConfig';
import { AccountInfo } from '@azure/msal-browser';
import { UserStorage, StoredUserInfo } from './userStorage';
import { useEffect, useState } from 'react';

export interface UserInfo {
  name: string;
  email: string;
  initials: string;
  roles?: string[];
}

export const useAuth = () => {
  const { instance, accounts, inProgress } = useMsal();
  const [storedUserInfo, setStoredUserInfo] = useState<StoredUserInfo | null>(null);

  const isAuthenticated = accounts.length > 0;
  const account: AccountInfo | null = accounts[0] || null;

  // Load stored user info on mount
  useEffect(() => {
    const stored = UserStorage.getUserInfo();
    setStoredUserInfo(stored);
  }, []);

  // Update stored user info when authentication state changes
  useEffect(() => {
    if (isAuthenticated && account) {
      const userInfo = extractUserInfoFromAccount(account);
      UserStorage.setUserInfo(userInfo);
      setStoredUserInfo({ ...userInfo, lastLogin: new Date().toISOString() });
    } else if (!isAuthenticated) {
      UserStorage.clearUserInfo();
      setStoredUserInfo(null);
    }
  }, [isAuthenticated, account]);

  // Helper function to extract user info from account
  const extractUserInfoFromAccount = (account: AccountInfo): UserInfo => {
    const name = account.name || account.username || 'Unknown User';
    const email = account.username || '';
    
    // Generate initials from name
    const initials = name
      .split(' ')
      .map(part => part.charAt(0).toUpperCase())
      .slice(0, 2)
      .join('');

    // Extract roles from ID token claims
    const idTokenClaims = account.idTokenClaims as any;
    const roles = idTokenClaims?.roles || [];

    return {
      name,
      email,
      initials: initials || email.charAt(0).toUpperCase(),
      roles,
    };
  };

  const login = async () => {
    try {
      // Try redirect flow first (better for SPA)
      await instance.loginRedirect(loginRedirectRequest);
    } catch (error) {
      console.error('Login failed:', error);
      // Fallback to popup if redirect fails
      try {
        await instance.loginPopup(loginRequest);
      } catch (popupError) {
        console.error('Popup login also failed:', popupError);
        throw popupError;
      }
    }
  };

  const logout = async () => {
    try {
      // Clear stored user info before logout
      UserStorage.clearUserInfo();
      setStoredUserInfo(null);
      
      // Use redirect logout (more reliable for SPA)
      await instance.logoutRedirect({
        account: account,
        postLogoutRedirectUri: window.location.origin,
      });
    } catch (error) {
      console.error('Logout failed:', error);
      throw error;
    }
  };

  const getAccessToken = async (): Promise<string | null> => {
    if (!account) return null;

    try {
      const response = await instance.acquireTokenSilent({
        ...apiRequest,
        account: account,
      });
      return response.accessToken;
    } catch (error) {
      try {
        // Fallback to popup (redirect doesn't return a token directly)
        const response = await instance.acquireTokenPopup({
          ...apiRequest,
          account: account,
        });
        return response.accessToken;
      } catch (popupError) {
        console.error('Token acquisition failed:', popupError);
        return null;
      }
    }
  };

  const getUserInfo = (): UserInfo | null => {
    // Return stored user info if available (faster and persists across refreshes)
    if (storedUserInfo) {
      return {
        name: storedUserInfo.name,
        email: storedUserInfo.email,
        initials: storedUserInfo.initials,
        roles: storedUserInfo.roles,
      };
    }

    // Fallback to extracting from account if not stored yet
    if (account) {
      return extractUserInfoFromAccount(account);
    }

    return null;
  };

  const getStoredUserInfo = (): StoredUserInfo | null => {
    return storedUserInfo;
  };

  return {
    isAuthenticated,
    account,
    inProgress,
    login,
    logout,
    getAccessToken,
    getUserInfo,
    getStoredUserInfo,
  };
};