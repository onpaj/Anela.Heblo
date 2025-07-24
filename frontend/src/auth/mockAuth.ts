import { UserInfo } from './useAuth';

/**
 * Mock authentication for development testing
 * This simulates a successful login without using real credentials
 */
export const createMockUser = (): UserInfo => {
  return {
    name: 'Ondrej Pajgrt',
    email: 'ondra@anela.cz',
    initials: 'OP',
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