import React, { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useWeatherForecast } from '../useWeatherForecast';
import * as clientModule from '../../client';

const mockFetch = jest.fn();
const mockApiClient = {
  baseUrl: 'http://localhost:5001',
  http: { fetch: mockFetch },
};

jest.mock('../../client');

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('useWeatherForecast', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (clientModule.getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);
  });

  it('fetches and returns forecast days on success', async () => {
    const mockDays = [
      { date: '2024-06-01', cityName: 'Praha', minTemperatureCelsius: 15.0, maxTemperatureCelsius: 28.5, weatherCode: 0 },
      { date: '2024-06-02', cityName: 'Brno', minTemperatureCelsius: 13.0, maxTemperatureCelsius: 26.5, weatherCode: 3 },
    ];
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: jest.fn().mockResolvedValue({ success: true, days: mockDays }),
    });

    const { result } = renderHook(() => useWeatherForecast(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockDays);
    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5001/api/weather-forecast',
      expect.objectContaining({ method: 'GET' })
    );
  });

  it('sets isError when HTTP response is not ok', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 503 });

    const { result } = renderHook(() => useWeatherForecast(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('sets isError when API returns success=false', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: jest.fn().mockResolvedValue({ success: false, days: [] }),
    });

    const { result } = renderHook(() => useWeatherForecast(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
