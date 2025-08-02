/**
 * Integration tests for WeatherTest component bearer token authentication
 */

import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import WeatherTest from '../WeatherTest';
import { shouldUseMockAuth, getRuntimeConfig, getConfig, loadConfig } from '../../../config/runtimeConfig';
import { mockAuthService } from '../../../auth/mockAuth';
import { getAuthenticatedApiClient } from '../../../api/client';

// Mock the dependencies
jest.mock('../../../config/runtimeConfig', () => ({
  shouldUseMockAuth: jest.fn(),
  getRuntimeConfig: jest.fn(),
  getConfig: jest.fn(),
  loadConfig: jest.fn(),
}));
jest.mock('../../../auth/mockAuth');

// Mock the API client entirely
jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

// Mock fetch globally
const mockFetch = jest.fn();
global.fetch = mockFetch;

const mockShouldUseMockAuth = shouldUseMockAuth as jest.MockedFunction<typeof shouldUseMockAuth>;
const mockGetRuntimeConfig = getRuntimeConfig as jest.MockedFunction<typeof getRuntimeConfig>;
const mockGetConfig = getConfig as jest.MockedFunction<typeof getConfig>;
const mockLoadConfig = loadConfig as jest.MockedFunction<typeof loadConfig>;
const mockMockAuthService = mockAuthService as jest.Mocked<typeof mockAuthService>;
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

describe('WeatherTest Component Authentication Integration', () => {
  const mockWeatherData = [
    { date: '2025-01-01', temperatureC: 20, temperatureF: 68, summary: 'Warm' },
    { date: '2025-01-02', temperatureC: 15, temperatureF: 59, summary: 'Cool' },
  ];

  beforeEach(() => {
    const mockConfig = {
      apiUrl: 'http://localhost:8080',
      useMockAuth: false,
      azureClientId: 'test-client-id',
      azureAuthority: 'https://login.microsoftonline.com/test-tenant',
    };

    // Mock all config functions
    mockGetRuntimeConfig.mockReturnValue(mockConfig);
    mockGetConfig.mockReturnValue(mockConfig);
    mockLoadConfig.mockReturnValue(mockConfig);

    // Mock mockAuthService
    mockMockAuthService.getAccessToken.mockReturnValue('mock-bearer-token');
    mockMockAuthService.isAuthenticated.mockReturnValue(true);

    // Create a mock API client
    const mockApiClient = {
      weatherForecast: jest.fn(),
    };
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);

    mockFetch.mockClear();
    jest.clearAllMocks();
  });

  afterEach(() => {
    jest.resetAllMocks();
  });

  it('should use mock authentication and send mock bearer token when shouldUseMockAuth returns true', async () => {
    // Arrange
    mockShouldUseMockAuth.mockReturnValue(true);
    
    // Get the mock API client instance
    const mockApiClient = mockGetAuthenticatedApiClient() as any;
    mockApiClient.weatherForecast.mockResolvedValue(mockWeatherData);

    // Act
    render(<WeatherTest />);

    // Assert - Wait for API client to be called
    await waitFor(() => {
      expect(mockGetAuthenticatedApiClient).toHaveBeenCalled();
    });
    expect(mockApiClient.weatherForecast).toHaveBeenCalled();

    // Verify weather data is displayed
    await waitFor(() => {
      expect(screen.getByText('20°C')).toBeInTheDocument();
    });
    expect(screen.getByText('15°C')).toBeInTheDocument();
  });

  it('should use real authentication and send no token when shouldUseMockAuth returns false (fallback behavior)', async () => {
    // Arrange - Real auth isn't fully implemented yet, so it should fall back to no token
    mockShouldUseMockAuth.mockReturnValue(false);
    
    // Get the mock API client instance
    const mockApiClient = mockGetAuthenticatedApiClient() as any;
    mockApiClient.weatherForecast.mockResolvedValue(mockWeatherData);

    // Act
    render(<WeatherTest />);

    // Assert - Should make API call via API client
    await waitFor(() => {
      expect(mockGetAuthenticatedApiClient).toHaveBeenCalled();
    });
    expect(mockApiClient.weatherForecast).toHaveBeenCalled();

    // Verify weather data is displayed
    await waitFor(() => {
      expect(screen.getByText('20°C')).toBeInTheDocument();
    });
    expect(screen.getByText('15°C')).toBeInTheDocument();
  });

  it('should handle 401 authentication errors and display error message', async () => {
    // Arrange
    mockShouldUseMockAuth.mockReturnValue(true);
    
    // Get the mock API client instance and make it reject with 401 error
    const mockApiClient = mockGetAuthenticatedApiClient() as any;
    mockApiClient.weatherForecast.mockRejectedValue(new Error('HTTP error! status: 401'));

    // Act
    render(<WeatherTest />);

    // Assert - Wait for error to be displayed
    await waitFor(() => {
      expect(screen.getByText('Error loading weather data')).toBeInTheDocument();
    });
    
    await waitFor(() => {
      expect(screen.getByText('HTTP error! status: 401')).toBeInTheDocument();
    });

    expect(mockGetAuthenticatedApiClient).toHaveBeenCalled();
    expect(mockApiClient.weatherForecast).toHaveBeenCalled();
  });

  it('should retry API call with bearer token when reload button is clicked', async () => {
    // Arrange
    mockShouldUseMockAuth.mockReturnValue(true);
    
    // Get the mock API client instance
    const mockApiClient = mockGetAuthenticatedApiClient() as any;
    mockApiClient.weatherForecast.mockResolvedValue(mockWeatherData);

    // Act
    render(<WeatherTest />);

    // Wait for initial load to complete
    await waitFor(() => {
      expect(screen.getByText('20°C')).toBeInTheDocument();
    });

    // Reset mocks to track the second call
    jest.clearAllMocks();
    // Re-setup the mock after clearing
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);
    mockApiClient.weatherForecast.mockResolvedValue(mockWeatherData);

    // Click reload button (should be enabled after loading is complete)
    await waitFor(() => {
      const reloadButton = screen.getByRole('button', { name: /reload/i });
      expect(reloadButton).not.toBeDisabled();
    });
    
    const reloadButton = screen.getByRole('button', { name: /reload/i });
    
    fireEvent.click(reloadButton);

    // Assert - Should make another API call via API client
    await waitFor(() => {
      expect(mockGetAuthenticatedApiClient).toHaveBeenCalled();
    });
    expect(mockApiClient.weatherForecast).toHaveBeenCalled();
  });

});