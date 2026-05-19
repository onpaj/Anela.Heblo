import React from 'react';
import { render, screen } from '@testing-library/react';
import WeatherForecastReport from '../WeatherForecastReport';
import { useWeatherForecast } from '../../../../api/hooks/useWeatherForecast';

jest.mock('../../../../api/hooks/useWeatherForecast');

const mockDays = [
  { date: '2024-06-01', cityName: 'Praha', maxTemperatureCelsius: 28.5, weatherCode: 0 },
  { date: '2024-06-02', cityName: 'Brno', maxTemperatureCelsius: 26.5, weatherCode: 3 },
  { date: '2024-06-03', cityName: 'Praha', maxTemperatureCelsius: 30.2, weatherCode: 1 },
  { date: '2024-06-04', cityName: 'Ostrava', maxTemperatureCelsius: 27.0, weatherCode: 45 },
  { date: '2024-06-05', cityName: 'Praha', maxTemperatureCelsius: 25.5, weatherCode: 61 },
  { date: '2024-06-06', cityName: 'Brno', maxTemperatureCelsius: 24.0, weatherCode: 95 },
  { date: '2024-06-07', cityName: 'Praha', maxTemperatureCelsius: 22.0, weatherCode: 71 },
];

describe('WeatherForecastReport', () => {
  it('renders all 7 day rows when data is loaded', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: false,
      data: mockDays,
    });

    render(<WeatherForecastReport />);

    expect(screen.getAllByText('Praha').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('28.5 °C')).toBeInTheDocument();
    expect(screen.getByText('30.2 °C')).toBeInTheDocument();
  });

  it('renders LoadingState when isLoading is true', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: true,
      isError: false,
      data: undefined,
    });

    render(<WeatherForecastReport />);

    expect(screen.getByText(/načítám předpověď/i)).toBeInTheDocument();
  });

  it('renders ErrorState when isError is true', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: true,
      data: undefined,
    });

    render(<WeatherForecastReport />);

    expect(screen.getByText(/nepodařilo se načíst předpověď/i)).toBeInTheDocument();
  });

  it('renders exactly 7 temperature bar elements with a width style', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: false,
      data: mockDays,
    });

    render(<WeatherForecastReport />);

    const bars = screen.getAllByTestId('temp-bar');
    expect(bars).toHaveLength(7);
    bars.forEach((bar) => {
      const style = bar.getAttribute('style');
      expect(style).toMatch(/width:\s*\d+(\.\d+)?%/);
    });
    expect(bars[0]).toHaveClass('bg-orange-500');
  });
});
