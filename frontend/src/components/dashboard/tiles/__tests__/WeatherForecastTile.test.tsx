import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { WeatherForecastTile, type WeatherForecastTileProps } from '../WeatherForecastTile';

const fiveDays = {
  status: 'success',
  data: {
    days: [
      { date: '2026-05-19', cityName: 'Brno', minTemperatureCelsius: 14, maxTemperatureCelsius: 18, weatherCode: 2 },
      { date: '2026-05-20', cityName: 'Praha', minTemperatureCelsius: 19, maxTemperatureCelsius: 26, weatherCode: 0 },
      { date: '2026-05-21', cityName: 'Brno', minTemperatureCelsius: 9, maxTemperatureCelsius: 11, weatherCode: 95 },
      { date: '2026-05-22', cityName: 'Praha', minTemperatureCelsius: 16, maxTemperatureCelsius: 23, weatherCode: 3 },
      { date: '2026-05-23', cityName: 'Brno', minTemperatureCelsius: 22, maxTemperatureCelsius: 31, weatherCode: 0 },
    ],
  },
};

function renderTile(data: WeatherForecastTileProps['data']) {
  return render(
    <MemoryRouter>
      <WeatherForecastTile data={data} />
    </MemoryRouter>,
  );
}

describe('WeatherForecastTile', () => {
  it('renders five forecast rows', () => {
    renderTile(fiveDays);
    expect(screen.getAllByText('Brno').length).toBeGreaterThanOrEqual(3);
    expect(screen.getAllByText('Praha').length).toBeGreaterThanOrEqual(2);
  });

  it('renders min and max temperature for each row', () => {
    renderTile(fiveDays);
    expect(screen.getByText('14°')).toBeInTheDocument();
    expect(screen.getByText('18°C')).toBeInTheDocument();
  });

  it('applies red color class for max temp >= 26°C', () => {
    renderTile(fiveDays);
    const bars = screen.getAllByTestId('temp-bar');
    const redBar = bars.find(b => b.className.includes('bg-red-500'));
    expect(redBar).toBeDefined();
  });

  it('applies amber color class for max temp in 16–20°C range', () => {
    renderTile(fiveDays);
    const bars = screen.getAllByTestId('temp-bar');
    // Day 1: max 18°C → bg-amber-400
    const amberBar = bars.find(b => b.className.includes('bg-amber-400'));
    expect(amberBar).toBeDefined();
  });

  it('renders a link to the cooling page', () => {
    renderTile(fiveDays);
    const link = screen.getByRole('link', { name: /chlaz/i });
    expect(link).toHaveAttribute('href', '/customer/cooling');
  });

  it('shows error message when status is error', () => {
    renderTile({ status: 'error', error: 'Předpověď počasí není dostupná.' });
    expect(screen.getByText('Předpověď počasí není dostupná.')).toBeInTheDocument();
  });

  it('shows empty state message when days array is empty', () => {
    renderTile({ status: 'success', data: { days: [] } });
    expect(screen.getByText('Žádná data')).toBeInTheDocument();
  });

  it('renders nothing when data.data is undefined', () => {
    const { container } = renderTile({ status: 'success' });
    expect(container.firstChild).toBeNull();
  });
});
