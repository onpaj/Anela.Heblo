/**
 * Tests for API client bearer token authentication
 */

import { ApiClient } from '../../src/api/generated/api-client';

// Mock fetch globally
const mockFetch = jest.fn();
global.fetch = mockFetch;

describe('ApiClient Bearer Token Authentication', () => {
  beforeEach(() => {
    mockFetch.mockClear();
  });

  afterEach(() => {
    jest.resetAllMocks();
  });

  it('should send bearer token in Authorization header when token provider is provided', async () => {
    // Arrange
    const mockToken = 'test-bearer-token';
    const mockTokenProvider = jest.fn().mockResolvedValue(mockToken);
    const mockResponse = [
      { date: '2025-01-01', temperatureC: 20, temperatureF: 68, summary: 'Warm' }
    ];

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockResponse),
    });

    const apiClient = new ApiClient('http://localhost:8080', mockTokenProvider);

    // Act
    await apiClient.weatherForecast();

    // Assert
    expect(mockTokenProvider).toHaveBeenCalledTimes(1);
    expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${mockToken}`,
      },
    });
  });

  it('should make request without Authorization header when no token provider is provided', async () => {
    // Arrange
    const mockResponse = [
      { date: '2025-01-01', temperatureC: 20, temperatureF: 68, summary: 'Warm' }
    ];

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockResponse),
    });

    const apiClient = new ApiClient('http://localhost:8080');

    // Act
    await apiClient.weatherForecast();

    // Assert
    expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
    });
  });

  it('should make request without Authorization header when token provider returns null', async () => {
    // Arrange
    const mockTokenProvider = jest.fn().mockResolvedValue(null);
    const mockResponse = [
      { date: '2025-01-01', temperatureC: 20, temperatureF: 68, summary: 'Warm' }
    ];

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockResponse),
    });

    const apiClient = new ApiClient('http://localhost:8080', mockTokenProvider);

    // Act
    await apiClient.weatherForecast();

    // Assert
    expect(mockTokenProvider).toHaveBeenCalledTimes(1);
    expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
    });
  });

  it('should handle token provider errors gracefully', async () => {
    // Arrange
    const mockTokenProvider = jest.fn().mockRejectedValue(new Error('Token acquisition failed'));
    const mockResponse = [
      { date: '2025-01-01', temperatureC: 20, temperatureF: 68, summary: 'Warm' }
    ];

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockResponse),
    });

    const consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation();
    const apiClient = new ApiClient('http://localhost:8080', mockTokenProvider);

    // Act
    await apiClient.weatherForecast();

    // Assert
    expect(mockTokenProvider).toHaveBeenCalledTimes(1);
    expect(consoleWarnSpy).toHaveBeenCalledWith('Failed to get access token:', expect.any(Error));
    expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
    });

    consoleWarnSpy.mockRestore();
  });

  it('should throw error when API returns non-ok response', async () => {
    // Arrange
    const mockToken = 'test-bearer-token';
    const mockTokenProvider = jest.fn().mockResolvedValue(mockToken);

    mockFetch.mockResolvedValue({
      ok: false,
      status: 401,
    });

    const apiClient = new ApiClient('http://localhost:8080', mockTokenProvider);

    // Act & Assert
    await expect(apiClient.weatherForecast()).rejects.toThrow('HTTP error! status: 401');
    expect(mockTokenProvider).toHaveBeenCalledTimes(1);
    expect(mockFetch).toHaveBeenCalledWith('http://localhost:8080/WeatherForecast', {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${mockToken}`,
      },
    });
  });

  it('should use correct base URL from constructor', async () => {
    // Arrange
    const customBaseUrl = 'https://api.example.com';
    const mockToken = 'test-bearer-token';
    const mockTokenProvider = jest.fn().mockResolvedValue(mockToken);
    const mockResponse = [
      { date: '2025-01-01', temperatureC: 20, temperatureF: 68, summary: 'Warm' }
    ];

    mockFetch.mockResolvedValue({
      ok: true,
      json: jest.fn().mockResolvedValue(mockResponse),
    });

    const apiClient = new ApiClient(customBaseUrl, mockTokenProvider);

    // Act
    await apiClient.weatherForecast();

    // Assert
    expect(mockFetch).toHaveBeenCalledWith(`${customBaseUrl}/WeatherForecast`, {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${mockToken}`,
      },
    });
  });
});