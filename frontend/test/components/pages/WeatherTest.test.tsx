/**
 * Integration tests for WeatherTest component bearer token authentication
 */

import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import WeatherTest from '../../../src/components/pages/WeatherTest';
import { useAuth } from '../../../src/auth/useAuth';
import { useMockAuth } from '../../../src/auth/mockAuth';
import { shouldUseMockAuth, getRuntimeConfig } from '../../../src/config/runtimeConfig';

// Mock the dependencies
jest.mock('../../../src/auth/useAuth');
jest.mock('../../../src/auth/mockAuth');
jest.mock('../../../src/config/runtimeConfig');

// Mock fetch globally
const mockFetch = jest.fn();
global.fetch = mockFetch;

const mockUseAuth = useAuth as jest.MockedFunction<typeof useAuth>;
const mockUseMockAuth = useMockAuth as jest.MockedFunction<typeof useMockAuth>;
const mockShouldUseMockAuth = shouldUseMockAuth as jest.MockedFunction<typeof shouldUseMockAuth>;
const mockGetRuntimeConfig = getRuntimeConfig as jest.MockedFunction<typeof getRuntimeConfig>;

describe('WeatherTest Component Authentication Integration', () => {
  const mockWeatherData = [
    { date: '2025-01-01', temperatureC: 20, temperatureF: 68, summary: 'Warm' },
    { date: '2025-01-02', temperatureC: 15, temperatureF: 59, summary: 'Cool' },
  ];

  beforeEach(() => {
    // Mock runtime config
    mockGetRuntimeConfig.mockReturnValue({
      apiUrl: 'http://localhost:8080',
      useMockAuth: false,
      azureClientId: 'test-client-id',
      azureAuthority: 'https://login.microsoftonline.com/test-tenant',
    });

    mockFetch.mockClear();
  });

  afterEach(() => {
    jest.resetAllMocks();
  });

  it('should use mock authentication and send mock bearer token when shouldUseMockAuth returns true', async () => {
    // Arrange
    const mockToken = 'mock-access-token';
    const mockGetAccessToken = jest.fn().mockResolvedValue(mockToken);

    mockShouldUseMockAuth.mockReturnValue(true);
    mockUseMockAuth.mockReturnValue({
      getAccessToken: mockGetAccessToken,
      isAuthenticated: true,
      getUserInfo: jest.fn().mockReturnValue({ name: 'Mock User', email: 'mock@test.com', initials: 'MU' }),
      login: jest.fn(),
      logout: jest.fn(),
    });
    mockUseAuth.mockReturnValue({
      getAccessToken: jest.fn(),
      isAuthenticated: false,
      getUserInfo: jest.fn(),
      login: jest.fn(),
      logout: jest.fn(),
      account: null,
      inProgress: 'none',
      getStoredUserInfo: jest.fn(),
    });

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockWeatherData),
    });

    // Act
    render(<WeatherTest />);

    // Assert - Wait for initial API call
    await waitFor(() => {
      expect(mockGetAccessToken).toHaveBeenCalledTimes(1);
      expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${mockToken}`,
        },
      });
    });

    // Verify weather data is displayed
    expect(screen.getByText('20°C')).toBeInTheDocument();
    expect(screen.getByText('15°C')).toBeInTheDocument();
  });

  it('should use real authentication and send Azure AD bearer token when shouldUseMockAuth returns false', async () => {
    // Arrange
    const mockToken = 'real-azure-ad-token';
    const mockGetAccessToken = jest.fn().mockResolvedValue(mockToken);

    mockShouldUseMockAuth.mockReturnValue(false);
    mockUseAuth.mockReturnValue({
      getAccessToken: mockGetAccessToken,
      isAuthenticated: true,
      getUserInfo: jest.fn().mockReturnValue({ name: 'Real User', email: 'real@company.com', initials: 'RU' }),
      login: jest.fn(),
      logout: jest.fn(),
      account: { homeAccountId: 'test', environment: 'test', tenantId: 'test', username: 'test' },
      inProgress: 'none',
      getStoredUserInfo: jest.fn(),
    });
    mockUseMockAuth.mockReturnValue({
      getAccessToken: jest.fn(),
      isAuthenticated: false,
      getUserInfo: jest.fn(),
      login: jest.fn(),
      logout: jest.fn(),
    });

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockWeatherData),
    });

    // Act
    render(<WeatherTest />);

    // Assert - Wait for initial API call
    await waitFor(() => {
      expect(mockGetAccessToken).toHaveBeenCalledTimes(1);
      expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${mockToken}`,
        },
      });
    });

    // Verify weather data is displayed
    expect(screen.getByText('20°C')).toBeInTheDocument();
    expect(screen.getByText('15°C')).toBeInTheDocument();
  });

  it('should handle 401 authentication errors and display error message', async () => {
    // Arrange
    const mockToken = 'invalid-token';
    const mockGetAccessToken = jest.fn().mockResolvedValue(mockToken);

    mockShouldUseMockAuth.mockReturnValue(true);
    mockUseMockAuth.mockReturnValue({
      getAccessToken: mockGetAccessToken,
      isAuthenticated: true,
      getUserInfo: jest.fn(),
      login: jest.fn(),
      logout: jest.fn(),
    });
    mockUseAuth.mockReturnValue({
      getAccessToken: jest.fn(),
      isAuthenticated: false,
      getUserInfo: jest.fn(),
      login: jest.fn(),
      logout: jest.fn(),
      account: null,
      inProgress: 'none',
      getStoredUserInfo: jest.fn(),
    });

    mockFetch.mockResolvedValue({
      ok: false,
      status: 401,
    });

    // Act
    render(<WeatherTest />);

    // Assert - Wait for error to be displayed
    await waitFor(() => {
      expect(screen.getByText('Error loading weather data')).toBeInTheDocument();
      expect(screen.getByText('HTTP error! status: 401')).toBeInTheDocument();
    });

    expect(mockGetAccessToken).toHaveBeenCalledTimes(1);
    expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${mockToken}`,
      },
    });
  });

  it('should retry API call with bearer token when reload button is clicked', async () => {
    // Arrange
    const mockToken = 'test-bearer-token';
    const mockGetAccessToken = jest.fn().mockResolvedValue(mockToken);

    mockShouldUseMockAuth.mockReturnValue(true);
    mockUseMockAuth.mockReturnValue({
      getAccessToken: mockGetAccessToken,
      isAuthenticated: true,
      getUserInfo: jest.fn(),
      login: jest.fn(),
      logout: jest.fn(),
    });
    mockUseAuth.mockReturnValue({
      getAccessToken: jest.fn(),
      isAuthenticated: false,
      getUserInfo: jest.fn(),
      login: jest.fn(),
      logout: jest.fn(),
      account: null,
      inProgress: 'none',
      getStoredUserInfo: jest.fn(),
    });

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockWeatherData),
    });

    // Act
    render(<WeatherTest />);

    // Wait for initial load to complete
    await waitFor(() => {
      expect(mockGetAccessToken).toHaveBeenCalledTimes(1);
      expect(screen.getByText('20°C')).toBeInTheDocument();
    });

    // Reset mock to track the second call
    mockFetch.mockClear();
    mockGetAccessToken.mockClear();

    // Click reload button (should be enabled after loading is complete)
    await waitFor(() => {
      const reloadButton = screen.getByRole('button', { name: /reload/i });
      expect(reloadButton).not.toBeDisabled();
      fireEvent.click(reloadButton);
    });

    // Assert - Should make another API call with bearer token
    await waitFor(() => {
      expect(mockGetAccessToken).toHaveBeenCalledTimes(1);
      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${mockToken}`,
        },
      });
    });
  });

  it('should handle token acquisition failures gracefully', async () => {
    // Arrange
    const mockGetAccessToken = jest.fn().mockRejectedValue(new Error('Token acquisition failed'));

    mockShouldUseMockAuth.mockReturnValue(true);
    mockUseMockAuth.mockReturnValue({
      getAccessToken: mockGetAccessToken,
      isAuthenticated: true,
      getUserInfo: jest.fn(),
      login: jest.fn(),
      logout: jest.fn(),
    });
    mockUseAuth.mockReturnValue({
      getAccessToken: jest.fn(),
      isAuthenticated: false,
      getUserInfo: jest.fn(),
      login: jest.fn(),
      logout: jest.fn(),
      account: null,
      inProgress: 'none',
      getStoredUserInfo: jest.fn(),
    });

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockWeatherData),
    });

    const consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation();

    // Act
    render(<WeatherTest />);

    // Assert - Should make API call without Authorization header
    await waitFor(() => {
      expect(mockGetAccessToken).toHaveBeenCalledTimes(1);
      expect(consoleWarnSpy).toHaveBeenCalledWith('Failed to get access token:', expect.any(Error));
      expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });
    });

    consoleWarnSpy.mockRestore();
  });
});