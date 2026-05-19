import { useWeatherForecast } from '../../../api/hooks/useWeatherForecast';
import { getWeatherIcon } from './weatherIcons';
import LoadingState from '../../common/LoadingState';
import ErrorState from '../../common/ErrorState';
import { getTemperatureColor, getTemperatureRangeBar } from './temperatureScale';


function WeatherForecastReport() {
  const { data, isLoading, isError } = useWeatherForecast();

  if (isLoading) {
    return <LoadingState message="Načítám předpověď počasí..." className="h-40" />;
  }

  if (isError || !data) {
    return <ErrorState message="Nepodařilo se načíst předpověď počasí." className="h-40" />;
  }

  return (
    <div className="mx-4 mb-4 rounded-lg border border-gray-200 bg-white p-4">
      <h2 className="mb-3 text-sm font-semibold text-gray-700">
        Předpověď počasí — nejteplejší místo v ČR
      </h2>
      <div className="space-y-2">
        {data.map((day) => {
          const icon = getWeatherIcon(day.weatherCode);
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
              <span className="w-24 shrink-0 text-gray-500">{label}</span>
              <span className="shrink-0 text-base leading-none">{icon}</span>
              <span className="w-10 shrink-0 text-right text-gray-600">
                {Math.round(day.minTemperatureCelsius)}°C
              </span>
              <div className="relative flex-1 h-2 rounded-full bg-gray-100 overflow-hidden">
                <div
                  data-testid="temp-bar"
                  className={`absolute h-2 ${getTemperatureColor(day.maxTemperatureCelsius)}`}
                  style={{ left: `${left}%`, width: `${width}%` }}
                />
              </div>
              <span className="w-10 shrink-0 text-left font-medium text-gray-900">
                {Math.round(day.maxTemperatureCelsius)}°C
              </span>
              <span className="w-20 shrink-0 text-right text-gray-500">{day.cityName}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export default WeatherForecastReport;
