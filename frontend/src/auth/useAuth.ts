import { useMsal } from '@azure/msal-react';
import { loginRequest, apiRequest } from './msalConfig';
import { AccountInfo } from '@azure/msal-browser';

export interface UserInfo {
  name: string;
  email: string;
  initials: string;
  roles?: string[];
}

export const useAuth = () => {
  const { instance, accounts, inProgress } = useMsal();

  const isAuthenticated = accounts.length > 0;
  const account: AccountInfo | null = accounts[0] || null;

  const login = async () => {
    try {
      await instance.loginPopup(loginRequest);
    } catch (error) {
      console.error('Login failed:', error);
      throw error;
    }
  };

  const logout = async () => {
    try {
      await instance.logoutPopup({
        account: account,
        mainWindowRedirectUri: window.location.origin,
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
    if (!account) return null;

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

  return {
    isAuthenticated,
    account,
    inProgress,
    login,
    logout,
    getAccessToken,
    getUserInfo,
  };
};