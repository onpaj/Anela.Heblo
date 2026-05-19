import React from 'react';
import { Link } from 'react-router-dom';
import { getWeatherIcon } from '../../customer/cooling/weatherIcons';
import { getTemperatureColor, getTemperatureRangeBar } from '../../customer/cooling/temperatureScale';

interface ForecastDay {
  date: string;
  cityName: string;
  minTemperatureCelsius: number;
  maxTemperatureCelsius: number;
  weatherCode: number;
}

export interface WeatherForecastTileProps {
  data: {
    status?: string;
    error?: string;
    data?: {
      days: ForecastDay[];
    };
  };
}

export function WeatherForecastTile({ data }: WeatherForecastTileProps) {
  if (data.status === 'error') {
    return (
      <div className="flex h-full items-center justify-center p-4 text-center text-sm text-red-600">
        {data.error ?? 'Předpověď počasí není dostupná.'}
      </div>
    );
  }

  if (!data.data) return null;

  const { days } = data.data;

  return (
    <div className="flex h-full flex-col p-4">
      <div className="flex-1 space-y-2">
        {days.length === 0 ? (
          <p className="text-sm text-gray-400">Žádná data</p>
        ) : (
          days.map((day) => {
            const icon = getWeatherIcon(day.weatherCode);
            // new Date('YYYY-MM-DD') parses as UTC, shifting to the previous day in
            // negative-offset timezones. Construct in local time instead.
            const [year, month, dayNum] = day.date.split('-').map(Number);
            const dateObj = new Date(year, month - 1, dayNum);
            const label = dateObj.toLocaleDateString('cs-CZ', {
              weekday: 'short',
              day: 'numeric',
              month: 'numeric',
            });
            const { left, width } = getTemperatureRangeBar(
              day.minTemperatureCelsius,
              day.maxTemperatureCelsius,
            );

            return (
              <div key={day.date} className="flex items-center gap-3 text-sm">
                <span className="w-14 shrink-0 text-gray-500">{label}</span>
                <span className="shrink-0 text-base leading-none">{icon}</span>
                <span className="w-8 shrink-0 text-right text-gray-600">
                  {Math.round(day.minTemperatureCelsius)}°
                </span>
                <div className="relative h-2 flex-1 overflow-hidden rounded-full bg-gray-100">
                  <div
                    data-testid="temp-bar"
                    className={`absolute h-2 ${getTemperatureColor(day.maxTemperatureCelsius)}`}
                    style={{ left: `${left}%`, width: `${width}%` }}
                  />
                </div>
                <span className="w-8 shrink-0 text-left font-medium text-gray-900">
                  {Math.round(day.maxTemperatureCelsius)}°C
                </span>
                <span className="w-12 shrink-0 text-right text-xs text-gray-400">
                  {day.cityName}
                </span>
              </div>
            );
          })
        )}
      </div>
      <div className="mt-3 border-t border-gray-100 pt-2 text-right">
        <Link
          to="/customer/cooling"
          className="text-xs text-gray-400 hover:text-gray-600"
        >
          → Zásilky s chlazením
        </Link>
      </div>
    </div>
  );
}
