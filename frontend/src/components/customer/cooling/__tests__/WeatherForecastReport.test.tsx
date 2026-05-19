import React from 'react';
import { render, screen } from '@testing-library/react';
import WeatherForecastReport from '../WeatherForecastReport';
import { useWeatherForecast } from '../../../../api/hooks/useWeatherForecast';

jest.mock('../../../../api/hooks/useWeatherForecast');

const mockDays = [
  { date: '2024-06-01', cityName: 'Praha',   minTemperatureCelsius: 18.0, maxTemperatureCelsius: 28.5, weatherCode: 0 },
  { date: '2024-06-02', cityName: 'Brno',    minTemperatureCelsius: 16.0, maxTemperatureCelsius: 26.5, weatherCode: 3 },
  { date: '2024-06-03', cityName: 'Praha',   minTemperatureCelsius: 20.0, maxTemperatureCelsius: 30.2, weatherCode: 1 },
  { date: '2024-06-04', cityName: 'Ostrava', minTemperatureCelsius: 15.0, maxTemperatureCelsius: 27.0, weatherCode: 45 },
  { date: '2024-06-05', cityName: 'Praha',   minTemperatureCelsius: 14.0, maxTemperatureCelsius: 25.5, weatherCode: 61 },
  { date: '2024-06-06', cityName: 'Brno',    minTemperatureCelsius: 12.0, maxTemperatureCelsius: 24.0, weatherCode: 95 },
  { date: '2024-06-07', cityName: 'Praha',   minTemperatureCelsius: 10.0, maxTemperatureCelsius: 22.0, weatherCode: 71 },
];

describe('WeatherForecastReport', () => {
  it('renders all 7 day rows with min and max temperature labels', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: false,
      data: mockDays,
    });

    render(<WeatherForecastReport />);

    expect(screen.getAllByText('Praha').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('29°C')).toBeInTheDocument(); // Math.round(28.5)
    expect(screen.getByText('30°C')).toBeInTheDocument(); // Math.round(30.2)
    expect(screen.getByText('18°C')).toBeInTheDocument(); // Math.round(18.0) min for day 1
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

  it('renders exactly 7 temperature bars with left offset and width styles', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: false,
      data: mockDays,
    });

    render(<WeatherForecastReport />);

    const bars = screen.getAllByTestId('temp-bar');
    expect(bars).toHaveLength(7);
    bars.forEach((bar) => {
      expect(bar.getAttribute('style')).toMatch(/left:\s*\d+(\.\d+)?%/);
      expect(bar.getAttribute('style')).toMatch(/width:\s*\d+(\.\d+)?%/);
    });
    // Day 1: min=18°C → left=45% (non-zero confirms it's a range bar, not 0→max)
    expect(bars[0].getAttribute('style')).toMatch(/left:\s*4[0-9]/);
    expect(bars[0]).toHaveClass('bg-red-500'); // 28.5°C → ≥26 → red
  });
});
