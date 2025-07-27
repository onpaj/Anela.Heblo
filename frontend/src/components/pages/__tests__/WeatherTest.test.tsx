/**
 * Integration tests for WeatherTest component bearer token authentication
 */

import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import WeatherTest from '../WeatherTest';
import { shouldUseMockAuth, getRuntimeConfig, getConfig, loadConfig } from '../../../config/runtimeConfig';
import { mockAuthService } from '../../../auth/mockAuth';

// Mock the dependencies
jest.mock('../../../config/runtimeConfig', () => ({
  shouldUseMockAuth: jest.fn(),
  getRuntimeConfig: jest.fn(),
  getConfig: jest.fn(),
  loadConfig: jest.fn(),
}));
jest.mock('../../../auth/mockAuth');

// Mock fetch globally
const mockFetch = jest.fn();
global.fetch = mockFetch;

const mockShouldUseMockAuth = shouldUseMockAuth as jest.MockedFunction<typeof shouldUseMockAuth>;
const mockGetRuntimeConfig = getRuntimeConfig as jest.MockedFunction<typeof getRuntimeConfig>;
const mockGetConfig = getConfig as jest.MockedFunction<typeof getConfig>;
const mockLoadConfig = loadConfig as jest.MockedFunction<typeof loadConfig>;
const mockMockAuthService = mockAuthService as jest.Mocked<typeof mockAuthService>;

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

    mockFetch.mockClear();
  });

  afterEach(() => {
    jest.resetAllMocks();
  });

  it('should use mock authentication and send mock bearer token when shouldUseMockAuth returns true', async () => {
    // Arrange
    mockShouldUseMockAuth.mockReturnValue(true);
    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockWeatherData),
    });

    // Act
    render(<WeatherTest />);

    // Assert - Wait for initial API call with mock token from mockAuthService
    await waitFor(() => {
      expect(mockMockAuthService.getAccessToken).toHaveBeenCalled();
      expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/api/weather/forecast', {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer mock-bearer-token',
        },
      });
    });

    // Verify weather data is displayed
    expect(screen.getByText('20°C')).toBeInTheDocument();
    expect(screen.getByText('15°C')).toBeInTheDocument();
  });

  it('should use real authentication and send no token when shouldUseMockAuth returns false (fallback behavior)', async () => {
    // Arrange - Real auth isn't fully implemented yet, so it should fall back to no token
    mockShouldUseMockAuth.mockReturnValue(false);
    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockWeatherData),
    });

    // Act
    render(<WeatherTest />);

    // Assert - Should make API call without token (fallback behavior)
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/api/weather/forecast', {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });
    });

    // Verify weather data is displayed
    expect(screen.getByText('20°C')).toBeInTheDocument();
    expect(screen.getByText('15°C')).toBeInTheDocument();
  });

  it('should handle 401 authentication errors and display error message', async () => {
    // Arrange
    mockShouldUseMockAuth.mockReturnValue(true);
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

    expect(mockMockAuthService.getAccessToken).toHaveBeenCalled();
    expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/api/weather/forecast', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer mock-bearer-token',
      },
    });
  });

  it('should retry API call with bearer token when reload button is clicked', async () => {
    // Arrange
    mockShouldUseMockAuth.mockReturnValue(true);
    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockWeatherData),
    });

    // Act
    render(<WeatherTest />);

    // Wait for initial load to complete
    await waitFor(() => {
      expect(screen.getByText('20°C')).toBeInTheDocument();
    });

    // Reset mocks to track the second call
    mockFetch.mockClear();
    mockMockAuthService.getAccessToken.mockClear();

    // Click reload button (should be enabled after loading is complete)
    await waitFor(() => {
      const reloadButton = screen.getByRole('button', { name: /reload/i });
      expect(reloadButton).not.toBeDisabled();
      fireEvent.click(reloadButton);
    });

    // Assert - Should make another API call with mock bearer token
    await waitFor(() => {
      expect(mockMockAuthService.getAccessToken).toHaveBeenCalled();
      expect(mockFetch).toHaveBeenCalledTimes(1);
      expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/api/weather/forecast', {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer mock-bearer-token',
        },
      });
    });
  });

});